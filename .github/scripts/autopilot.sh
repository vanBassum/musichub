#!/usr/bin/env bash
# The autopilot state machine: one attempt at one issue, started by the `autopilot`
# label (see .github/workflows/autopilot.yml and "Autopilot" in CLAUDE.md).
#
#   autopilot.sh precheck  the label was added: check who, the mode, the Actions budget
#   autopilot.sh start     the same, plus Claude's usage status; then start the attempt
#   autopilot.sh decide    act on Claude's analysis: build it, or back to To Do; for an
#                          investigation: post the report on the issue, back to To Do
#   autopilot.sh publish   push Claude's commits and open the PR, or back to To Do
#   autopilot.sh outcome   the review run of the PR ended: Done; the one fix round;
#                          or back to To Do
#   autopilot.sh fix-precheck, fix-start, fix-publish
#                          the fix round (.github/workflows/autopilot-fix.yml): check
#                          it may run, collect the findings; push Claude's fix to the
#                          same PR for a full new review, or back to To Do
#   autopilot.sh gate      for review-gate.sh: refuse an autopilot PR that is not its
#                          issue's active attempt
#
# THE STATE is the newest marker comment the workflow (github-actions[bot]) left on
# the issue:   <!-- autopilot: issue=N run=R stage=S pr=P -->
# where R is the attempt's workflow run, S one of analysis, build, merge, fix, done,
# todo, and P the attempt's PR (or -). Every step first checks that the state is the
# one it expects, so a rerun, a late or a superseded run changes nothing. The merge
# gate merges an autopilot PR only while the state is `merge` for that PR and attempt
# and the issue still carries the label: writing any newer marker stops it.
#
# THE FIX ROUND: when the first review of an attempt's PR blocks it with findings a
# fix can address, `outcome` moves the attempt to `fix` and starts the fix workflow,
# which pushes Claude's fix to the same PR and moves it back to `merge`; all four
# reviewers and the gate then judge the new commit afresh. An attempt gets at most
# one: once a `fix` marker of the attempt exists (comments are never rewritten), no
# later review, rerun or racing event grants another, and the next blocked review
# stops the attempt. While the state is `fix`, nothing merges.
#
# Text Claude wrote is posted with `<!--` defused, so it can't forge a marker.
#
# AN INVESTIGATION is an issue that also carries `autopilot:analysis-only`: the
# analysis step investigates and writes a report instead of admitting a change, and
# `decide` ends the attempt there (To Do -> Analysis -> To Do). The report is posted
# on the issue and that is all: nothing is built, no branch or PR is made, no issue
# is created and the issue stays open. Follow-up work appears only as suggestions in
# the report's text; a human decides which of it becomes an issue.
#
# Tokens: GH_TOKEN is the workflow's GITHUB_TOKEN (comments, labels, markers: they
# come from github-actions[bot] and trigger nothing). AUTOPILOT_TOKEN (the
# AUTOPILOT_PAT secret) pushes the branch and opens the PR: a branch or PR made with
# GITHUB_TOKEN starts no workflows (the reviewers and the build check would never
# run). AUTOPILOT_BOARD_TOKEN moves the board card: neither GITHUB_TOKEN nor a
# fine-grained token can reach a user's project.
#
# Tested by .github/scripts/autopilot.test.sh.
set -euo pipefail

fail() { echo "::error::$*" >&2; exit 1; }
warn() { echo "::warning::$*" >&2; }

R=${GITHUB_REPOSITORY:-}
[[ $R =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail "GITHUB_REPOSITORY is not set."
API="repos/$R"
SERVER=${GITHUB_SERVER_URL:-https://github.com}
RUN_URL="$SERVER/$R/actions/runs/${GITHUB_RUN_ID:-0}"
WORK=${AUTOPILOT_DIR:-/tmp/autopilot}
PROJECT_ID=${AUTOPILOT_PROJECT_ID:-}   # the board: a project of the repository's owner
BRANCH_RE='^autopilot/issue-([1-9][0-9]*)-r([1-9][0-9]*)$'
# The review mechanism and the local guards are never changed autonomously (the
# merge gate refuses those anyway). Schema, authorization and other sensitive code
# are not refused by path: the analysis admits them by risk, and the reviewers hold
# them to the understanding (see .github/autopilot/analysis.md, 2.6).
GUARDED='^(\.github|\.githooks|\.claude)/'
# There is no database yet. Once EF Core migrations exist they live here, and
# publish refuses the unsafe ones (unsafe_migrations).
MIGRATIONS=musichub-api/Data/Migrations
# The only comments trusted for state markers and verdicts: the workflow's own.
# A person, or Claude through a comment, can't post as github-actions[bot].
FROM_WORKFLOW='select(.user.login == "github-actions[bot]" and .user.type == "Bot")'

need() { local v; for v in "$@"; do [ -n "${!v:-}" ] || fail "$v is not set."; done; }
number() { [[ ${!1:-} =~ ^[1-9][0-9]*$ ]] || fail "$1 is not a number: '${!1:-}'."; }
output() { echo "$1=$2" >>"${GITHUB_OUTPUT:-/dev/null}"; }
# Claude's text, made safe to post: no marker can be forged, and it fits in a
# comment or PR body (65536 characters).
defuse() { local t; t=$(sed 's/<!--/\&lt;!--/g'); printf '%s\n' "${t:0:60000}"; }

# --- reading ------------------------------------------------------------------
labels_of() { gh api "$API/issues/$1/labels" --paginate --jq '.[].name'; }
has_label() { local l; l=$(labels_of "$ISSUE") || fail "Could not read the labels of issue #$ISSUE."; grep -qx "${1:-autopilot}" <<<"$l"; }
INVESTIGATION_LABEL=autopilot:analysis-only

# The newest state marker on issue $1, as "run stage pr", or nothing. Callers
# capture it first (st=$(state_of N)) so that a failed API call stops the script.
state_of() {
  local bodies
  bodies=$(gh api "$API/issues/$1/comments" --paginate \
    --jq ".[] | $FROM_WORKFLOW | .body") \
    || fail "Could not read the comments of issue #$1."
  { grep -oE "<!-- autopilot: issue=$1 run=[0-9]+ stage=[a-z]+ pr=([0-9]+|-) -->" <<<"$bodies" || true; } \
    | tail -n1 | sed -E 's/.* run=([0-9]+) stage=([a-z]+) pr=([0-9]+|-) -->/\1 \2 \3/'
}

# expect <stage>: carry on only if this attempt ($ATTEMPT) is in <stage>.
expect() {
  local st run stage pr
  st=$(state_of "$ISSUE")
  read -r run stage pr <<<"$st" || true
  if [ "${run:-} ${stage:-}" != "$ATTEMPT $1" ]; then
    echo "Issue #$ISSUE is not in $1 for attempt $ATTEMPT (state: ${stage:-none}, run ${run:-none}); nothing to do."
    return 1
  fi
}

# expect_fix: carry on only if this attempt ($ATTEMPT) is in its fix round for $PR.
expect_fix() {
  local st run stage pr
  st=$(state_of "$ISSUE")
  read -r run stage pr <<<"$st" || true
  if [ "${run:-} ${stage:-} ${pr:-}" != "$ATTEMPT fix $PR" ]; then
    echo "Issue #$ISSUE is not in its fix round for PR #$PR, attempt $ATTEMPT (state: ${stage:-none} ${pr:-} run ${run:-none}); nothing to do."
    return 1
  fi
}

# fix_round_used: has attempt $ATTEMPT of issue $ISSUE ever been in `fix`? Any such
# marker counts, not only the newest: this is the one-round budget.
fix_round_used() {
  local bodies
  bodies=$(gh api "$API/issues/$ISSUE/comments" --paginate \
    --jq ".[] | $FROM_WORKFLOW | .body") \
    || fail "Could not read the comments of issue #$ISSUE."
  grep -qE "<!-- autopilot: issue=$ISSUE run=$ATTEMPT stage=fix pr=([0-9]+|-) -->" <<<"$bodies"
}

# active_attempt: is PR $PR the active attempt ($ATTEMPT) of issue $ISSUE, waiting
# in merge? The merge gate and the outcome report both ask exactly this. Leaves
# the state it found in ACTIVE_STATE for their messages.
active_attempt() {
  local st run stage pr
  st=$(state_of "$ISSUE")
  read -r run stage pr <<<"$st" || true
  ACTIVE_STATE="${stage:-none} ${pr:-} run ${run:-none}"
  [ "${stage:-} ${pr:-} ${run:-}" = "merge $PR $ATTEMPT" ]
}

# --- writing ------------------------------------------------------------------
# note <text>: a comment that doesn't change the state.
note() {
  printf '%s\n' "$1" >"$WORK/note.md"
  gh issue comment "$ISSUE" --repo "$R" --body-file "$WORK/note.md" >/dev/null || fail "Could not comment on issue #$ISSUE."
}
# say <stage> <pr|-> <text>: a comment that moves the attempt to <stage>.
say() {
  printf '%s\n\n<!-- autopilot: issue=%s run=%s stage=%s pr=%s -->\n' "$3" "$ISSUE" "$ATTEMPT" "$1" "$2" >"$WORK/say.md"
  gh issue comment "$ISSUE" --repo "$R" --body-file "$WORK/say.md" >/dev/null \
    || fail "Could not record stage $1 on issue #$ISSUE."
  echo "Issue #$ISSUE: attempt $ATTEMPT -> $1"
}
drop_label() { gh issue edit "$ISSUE" --repo "$R" --remove-label autopilot >/dev/null || warn "Could not remove the autopilot label."; }

# board <status> [issue]: move the card of the issue (default: this one). The board
# is the view, not the state, so a failure here is a warning, never a reason to stop.
# AUTOPILOT_BOARD_TOKEN is a classic token with only the `project` scope: a
# fine-grained token can't reach a project that belongs to a user.
board() {
  local n=${2:-$ISSUE}
  if [ -z "${AUTOPILOT_BOARD_TOKEN:-}" ]; then warn "AUTOPILOT_BOARD_TOKEN is not set; the card of issue #$n stays where it is."; return 0; fi
  if [ -z "$PROJECT_ID" ]; then warn "AUTOPILOT_PROJECT_ID is not set; the card of issue #$n stays where it is."; return 0; fi
  GH_TOKEN=$AUTOPILOT_BOARD_TOKEN board_move "$1" "$n" || warn "Could not move issue #$n to '$1' on the board."
}
board_move() {
  local content item field option
  content=$(gh api "$API/issues/$2" --jq .node_id) || return 1
  item=$(gh api graphql -f query='mutation($p:ID!,$c:ID!){addProjectV2ItemById(input:{projectId:$p,contentId:$c}){item{id}}}' \
    -f p="$PROJECT_ID" -f c="$content" --jq .data.addProjectV2ItemById.item.id) || return 1
  read -r field option < <(gh api graphql -f query='query($p:ID!){node(id:$p){... on ProjectV2{field(name:"Status"){... on ProjectV2SingleSelectField{id options{id name}}}}}}' \
    -f p="$PROJECT_ID" --jq ".data.node.field | .id + \" \" + ([.options[] | select(.name == \"$1\") | .id] | first // \"\")") || return 1
  [ -n "${item:-}" ] && [ -n "${option:-}" ] || return 1
  gh api graphql -f query='mutation($p:ID!,$i:ID!,$f:ID!,$o:String!){updateProjectV2ItemFieldValue(input:{projectId:$p,itemId:$i,fieldId:$f,value:{singleSelectOptionId:$o}}){projectV2Item{id}}}' \
    -f p="$PROJECT_ID" -f i="$item" -f f="$field" -f o="$option" >/dev/null || return 1
  echo "Issue #$2 -> board '$1'"
}

# to_do <class|-> <pr|-> <text>: end the attempt and hand the issue back. The marker
# goes first: from then on nothing of this attempt can merge, even if the label or
# board calls below fail.
to_do() {
  say todo "$2" "$3"
  if [ "$2" != - ]; then
    gh pr edit "$2" --repo "$R" --add-label no-automerge >/dev/null || warn "Could not label PR #$2 no-automerge (its attempt is stopped anyway)."
  fi
  if [ "$1" != - ]; then
    gh issue edit "$ISSUE" --repo "$R" --add-label "autopilot:$1" >/dev/null || warn "Could not add autopilot:$1."
  fi
  drop_label
  board "To Do"
}
# stopped <stage> <reason> [pr]: a failure; back to To Do and exit.
stopped() {
  local pr=${3:--} links="[Run]($RUN_URL)"
  [ "$pr" = - ] || links="$links · PR #$pr (kept for investigation, labelled \`no-automerge\`)"
  to_do blocked "$pr" "$(with_usage "**Autopilot stopped during $1; back to To Do.**

$2

$links")"
  exit 0
}

# --- budget: GitHub Actions -----------------------------------------------------
# A new attempt is admitted only if the account's Actions usage this month (all
# repositories of the owner share the allowance), plus a margin for usage GitHub hasn't
# reported yet, plus a reservation for this attempt and every autopilot run still
# going, stays within AUTOPILOT_ACTIONS_LIMIT_PCT (default 50) of the included
# minutes. Usage is counted in Linux-equivalent minutes: the gross amount of all
# Actions minute SKUs divided by the Linux per-minute price, so Windows and macOS
# minutes weigh in at their price. Anything unreadable refuses (BUDGET_REASON).
#
# The repository belongs to a user, not an organization. AUTOPILOT_BILLING_TOKEN is
# that user's own token (AUTOPILOT_PAT, with the Plan: read permission): only it can
# read the user's billing usage and plan. The included minutes come from
# AUTOPILOT_ACTIONS_INCLUDED_MINUTES or, when that is unset, from the user's plan: no
# API reports the allowance itself. Jobs on the self-hosted runner use no minutes.
actions_budget() {
  local owner=${R%%/*} token=${AUTOPILOT_BILLING_TOKEN:-} pct=${AUTOPILOT_ACTIONS_LIMIT_PCT:-50}
  local lag=${AUTOPILOT_ACTIONS_LAG_MINUTES:-60} per=${AUTOPILOT_ATTEMPT_RESERVE_MINUTES:-150}
  local included=${AUTOPILOT_ACTIONS_INCLUDED_MINUTES:-} plan usage gross price used active reserve projected limit v
  BUDGET_REASON="" BUDGET_SUMMARY=""
  for v in pct lag per; do
    [[ ${!v} =~ ^[0-9]{1,6}$ ]] || { BUDGET_REASON="a budget setting is not a whole number: '${!v}'"; return 1; }
  done
  [ "$pct" -le 100 ] || { BUDGET_REASON="AUTOPILOT_ACTIONS_LIMIT_PCT is above 100: '$pct'"; return 1; }
  [ -n "$token" ] || { BUDGET_REASON="there is no billing token (is the secret AUTOPILOT_PAT set?)"; return 1; }
  if [ -z "$included" ]; then
    # GET /user: the plan of the token's own user, who must own the repository.
    plan=$(GH_TOKEN=$token gh api user --jq 'if .login == "'"$owner"'" then .plan.name // "" else "" end' 2>/dev/null) || plan=""
    case $plan in
      free) included=2000 ;;
      pro) included=3000 ;;
      *) BUDGET_REASON="the plan of $owner could not be read ('${plan:-nothing}'; is AUTOPILOT_PAT a token of $owner?); set AUTOPILOT_ACTIONS_INCLUDED_MINUTES"; return 1 ;;
    esac
  fi
  [[ $included =~ ^[1-9][0-9]{0,6}$ ]] || { BUDGET_REASON="AUTOPILOT_ACTIONS_INCLUDED_MINUTES is not a number: '$included'"; return 1; }
  usage=$(GH_TOKEN=$token gh api "users/$owner/settings/billing/usage?year=$(date -u +%Y)&month=$(date -u +%-m)" \
    --jq '[.usageItems[] | select((.product | ascii_downcase) == "actions" and (.unitType | ascii_downcase) == "minutes")]
          | "\(map(.grossAmount) | add // 0) \([.[] | select(.sku | test("linux"; "i")) | .pricePerUnit] | max // 0.006)"' 2>/dev/null) \
    || { BUDGET_REASON="the Actions usage could not be read for $owner (does AUTOPILOT_PAT have Plan: read?)"; return 1; }
  read -r gross price <<<"$usage"
  [[ ${gross:-} =~ ^[0-9]+(\.[0-9]+)?([eE][-+]?[0-9]+)?$ && ${price:-} =~ ^0*[0-9]*\.?[0-9]+$ ]] \
    || { BUDGET_REASON="the usage report had an unexpected shape ('$usage')"; return 1; }
  active=$(gh api "$API/actions/workflows/autopilot.yml/runs?per_page=100" \
    --jq "[.workflow_runs[] | select(.status != \"completed\" and .id != ${GITHUB_RUN_ID:-0})] | length" 2>/dev/null) \
    || { BUDGET_REASON="the running autopilot attempts could not be listed"; return 1; }
  [[ $active =~ ^[0-9]+$ ]] || { BUDGET_REASON="the running autopilot attempts could not be counted"; return 1; }
  used=$(awk -v g="$gross" -v p="$price" 'BEGIN { if (p <= 0) exit 1; printf "%d", (g / p) + 0.999 }') \
    || { BUDGET_REASON="the Linux minute price in the usage report is zero"; return 1; }
  reserve=$((per * (active + 1)))
  projected=$((used + lag + reserve))
  limit=$((included * pct / 100))
  BUDGET_SUMMARY="Actions this month: $used of $included included minutes used ($((used * 100 / included))%); with $lag minutes for unreported usage and $reserve reserved for this attempt and $active other running attempt(s), that makes $projected of the $limit-minute limit ($pct%)."
  [ "$projected" -le "$limit" ] || { BUDGET_REASON="the Actions budget is used up. $BUDGET_SUMMARY The included minutes reset at the start of next month"; return 1; }
}

# --- budget: Claude ---------------------------------------------------------------
# The Claude subscription's session (5-hour) and weekly usage can't be read as a
# number: Claude Code reports it only as a status in its `rate_limit_event`, and
# raises it to `allowed_warning` (with the utilization) only when usage is high for
# that point in the window, on thresholds of Claude's own. Autopilot takes no new
# work, and doesn't start a build, while Claude reports anything but `allowed`, or
# reports that usage runs on extra (overage) credit.
#
# claude_status <file>: read the status claude-usage.sh extracted from a Claude run
# into CLAUDE_STATUS (allowed, allowed_warning, rejected or empty when the run
# reported none) and CLAUDE_STATUS_TEXT, a sentence for comments.
claude_status() {
  local status type util reset overage
  CLAUDE_STATUS="" CLAUDE_STATUS_TEXT=""
  [ -s "${1:-}" ] || return 0
  # "|" rather than a tab: read merges consecutive tabs, which would shift empty fields.
  IFS='|' read -r status type util reset overage < <(jq -r '[.limit_status // "", .limit_type // "",
      (.limit_utilization // "" | tostring), (.limit_resets_at // "" | tostring), (.limit_overage // false | tostring)]
      | map(gsub("[|\r\n]"; "")) | join("|")' "$1" 2>/dev/null | tr -d '\r') || true
  [ "${overage:-false}" = true ] && status=overage
  CLAUDE_STATUS=${status:-}
  CLAUDE_STATUS_TEXT="Claude reports usage status \`${status:-unknown}\`${type:+ for the ${type//_/-} limit}"
  [[ ${util:-} =~ ^[0-9.]+$ ]] && CLAUDE_STATUS_TEXT+=" at $(awk -v u="$util" 'BEGIN { printf "%d", u * 100 + 0.5 }')%"
  [[ ${reset:-} =~ ^[0-9]+$ ]] && CLAUDE_STATUS_TEXT+=", resetting $(date -u -d "@$reset" '+%Y-%m-%d %H:%M UTC')"
  return 0
}
# claude_gate <file>: before an attempt, with the status of a fresh probe. Anything
# but an explicit `allowed` refuses (CLAUDE_REASON). With AUTOPILOT_CLAUDE_AUTH
# `api-key`, analysis and build don't use the subscription, so there is no status.
claude_gate() {
  CLAUDE_REASON="" CLAUDE_SUMMARY=""
  if [ "${AUTOPILOT_CLAUDE_AUTH:-subscription}" = api-key ]; then
    CLAUDE_SUMMARY="Claude: analysis and build use the API key; its Console spend limit applies."
    return 0
  fi
  claude_status "${1:-}"
  case $CLAUDE_STATUS in
    allowed) CLAUDE_SUMMARY="$CLAUDE_STATUS_TEXT." ;;
    allowed_warning | rejected | overage) CLAUDE_REASON="$CLAUDE_STATUS_TEXT, so autopilot leaves the subscription to interactive use until it is back to \`allowed\`"; return 1 ;;
    *) CLAUDE_REASON="Claude's usage status could not be read (the probe run reported none), so autopilot doesn't start"; return 1 ;;
  esac
}

# usage_lines: one line per Claude run of this attempt, from the usage-*.json files
# the workflow extracted from claude-code-action's execution log. These are
# client-side measurements and estimates, not what the subscription has left. A run
# whose permissions refused tool calls says so, with the calls: in an unattended run
# that means a needed tool is missing from its --allowedTools (issue #301).
usage_lines() {
  local f
  for f in "$WORK"/usage-*.json; do
    [ -s "$f" ] || continue
    jq -r --arg name "$(basename "$f" .json | sed 's/^usage-//')" '
      def k: if . == null then "?" elif . >= 1000000 then "\(. / 100000 | floor / 10)M" elif . >= 1000 then "\(. / 1000 | floor)k" else tostring end;
      "- \($name): \(.turns // "?") turns, \(if .duration_ms then "\(.duration_ms / 6000 | floor / 10) min" else "? min" end), "
      + "\((.input // 0) + (.cache_read // 0) + (.cache_write // 0) | k) tokens in (\(.cache_read | k) from cache), \(.output | k) out, "
      + "≈ $\(if .cost_usd then (.cost_usd * 100 | round / 100) else "?" end) API-equivalent (estimate)"
      + (if (.denials | type) == "number" then "; \(.denials) tool call(s) refused by the run'"'"'s permissions (\(.denied // "?" | tostring | gsub("[<>`]"; " ")))" else "" end)' "$f" 2>/dev/null || true
  done
}
# with_usage <text>: <text> plus the usage lines, when there are any.
with_usage() {
  local lines; lines=$(usage_lines)
  if [ -n "$lines" ]; then printf '%s\n\n**Claude usage** (measured in the run; not the remaining allowance):\n%s' "$1" "$lines"; else printf '%s' "$1"; fi
}

# --- start --------------------------------------------------------------------
authorized() {
  local who=$1 users=${AUTOPILOT_USERS:-} perm
  [[ $who =~ ^[A-Za-z0-9-]+$ ]] || return 1
  if [ -n "${users//[ ,]/}" ]; then
    grep -qxF "$who" <<<"$(tr ' ,' '\n\n' <<<"$users")" || return 1
  fi
  perm=$(gh api "$API/collaborators/$who/permission" --jq .permission) || return 1
  case $perm in admin | maintain | write) return 0 ;; *) return 1 ;; esac
}

# admit: the checks that cost nothing but API calls (who, which issue, the mode,
# the Actions budget). On a refusal it comments, removes the label and returns 1.
admit() {
  local mode=${PIPELINE_MODE:-off} issue_state
  has_label || { echo "The autopilot label is already gone; nothing to do."; return 1; }
  if ! authorized "$SENDER"; then
    note "@$SENDER can't start autopilot: that needs write access to this repository${AUTOPILOT_USERS:+ and a place in AUTOPILOT_USERS}. The label is removed; nothing ran."
    drop_label; return 1
  fi
  issue_state=$(gh api "$API/issues/$ISSUE" --jq .state)
  if [ "$issue_state" != open ]; then note "This issue is $issue_state; autopilot only takes open issues. The label is removed."; drop_label; return 1; fi
  case $mode in
    off) note "Autopilot is switched off (\`PIPELINE_MODE\` is \`off\`). The label is removed; nothing ran."; drop_label; return 1 ;;
    analysis-only | full) ;;
    *) note "\`PIPELINE_MODE\` has an unknown value ('$mode'), so autopilot stays off. The label is removed."; drop_label; return 1 ;;
  esac
  if [ "$mode" = full ] && [ -z "${AUTOPILOT_TOKEN:-}" ]; then
    note "Autopilot can't run in \`full\` mode: it has no token to open a PR with (is the secret \`AUTOPILOT_PAT\` set?). The label is removed; nothing ran."
    drop_label; return 1
  fi
  if ! actions_budget; then
    note "**Autopilot did not start:** $BUDGET_REASON. The label is removed and the issue stays in To Do; nothing ran."
    drop_label; board "To Do"; return 1
  fi
}

# precheck: run before the Claude probe, so that a refused label costs no Claude
# usage at all. Outputs ok=true when the probe and `start` should run.
cmd_precheck() {
  need ISSUE SENDER GITHUB_RUN_ID; number ISSUE; ATTEMPT=$GITHUB_RUN_ID
  output ok false
  mkdir -p "$WORK"
  admit || return 0
  output ok true
}

cmd_start() {
  need ISSUE SENDER GITHUB_RUN_ID; number ISSUE; ATTEMPT=$GITHUB_RUN_ID
  local mode=${PIPELINE_MODE:-off} st run stage pr previous="" labels l investigation=false started
  output go false
  mkdir -p "$WORK"
  admit || return 0
  if ! claude_gate "${CLAUDE_STATUS_FILE:-$WORK/claude-status.json}"; then
    note "**Autopilot did not start:** $CLAUDE_REASON. The label is removed and the issue stays in To Do; nothing ran."
    drop_label; board "To Do"; return 0
  fi
  echo "$BUDGET_SUMMARY"
  echo "$CLAUDE_SUMMARY"

  st=$(state_of "$ISSUE")
  read -r run stage pr <<<"$st" || true
  if [ "${run:-}" = "$ATTEMPT" ]; then
    echo "This run already started attempt $ATTEMPT; a rerun never starts another. Add the label again for a new attempt."
    return 0
  fi
  case ${stage:-} in
    analysis | build) previous="

The previous attempt ([run]($SERVER/$R/actions/runs/$run)) ended without finishing; it is abandoned." ;;
    merge | fix) previous="

This supersedes the previous attempt: its PR #$pr can no longer merge automatically." ;;
  esac
  # The route is fixed here, and `decide` refuses to go on if the label changed since.
  labels=$(labels_of "$ISSUE") || fail "Could not read the labels of issue #$ISSUE."
  if grep -qx "$INVESTIGATION_LABEL" <<<"$labels"; then
    investigation=true
    started="**Autopilot investigation started** (\`$INVESTIGATION_LABEL\`: a report on this issue, nothing else; no new issues, no branch, no PR, no code change; mode \`$mode\`, [run]($RUN_URL))."
  else
    started="**Autopilot attempt started** (mode \`$mode\`, [run]($RUN_URL)). Checking the issue against the understanding."
  fi
  say analysis - "$started$previous

<sub>$BUDGET_SUMMARY $CLAUDE_SUMMARY</sub>"
  if [ "${stage:-}" = merge ] || [ "${stage:-}" = fix ]; then
    gh pr edit "$pr" --repo "$R" --add-label no-automerge >/dev/null || warn "Could not label PR #$pr no-automerge (it is superseded anyway)."
  fi
  for l in needs-design too-large blocked; do
    if grep -qx "autopilot:$l" <<<"$labels"; then gh issue edit "$ISSUE" --repo "$R" --remove-label "autopilot:$l" >/dev/null || true; fi
  done
  board Analysis
  output go true
  output mode "$mode"
  output investigation "$investigation"
}

# --- decide -------------------------------------------------------------------
# analysis_job_succeeded: stops the attempt unless the analysis job as a
# whole succeeded. Its output is then not even read: a job that failed after Claude
# finished (a post step, the upload) is a pipeline failure to look into, never an
# admission, however complete the analysis looks.
analysis_job_succeeded() {
  [ "${ANALYSIS_RESULT:-}" = success ] && return 0
  stopped analysis "The analysis job did not succeed (analysis job: ${ANALYSIS_RESULT:-unknown}), so its output is not used, even if it looks complete. This is a failure of the pipeline, not a verdict on the issue: see the run."
}

cmd_decide() {
  need ISSUE MODE GITHUB_RUN_ID; number ISSUE; ATTEMPT=$GITHUB_RUN_ID
  local file="$WORK/analysis.md" class="" classes body
  output build false
  mkdir -p "$WORK"
  expect analysis || return 0
  has_label || stopped analysis "The autopilot label was removed during the analysis, so the attempt is stopped."
  # The route `start` chose must still match the issue: a report is never built
  # from, and an admission is never posted as a report.
  local investigation=false
  has_label "$INVESTIGATION_LABEL" && investigation=true
  if [ "$investigation" != "${INVESTIGATION:-false}" ]; then
    stopped analysis "The \`$INVESTIGATION_LABEL\` label was $([ "$investigation" = true ] && echo added || echo removed) during the analysis, so this attempt no longer matches the issue. Nothing was built or created; add the \`autopilot\` label again to start over."
  fi
  if [ "$investigation" = true ]; then conclude_investigation; return 0; fi
  analysis_job_succeeded
  if [ -s "$file" ] && [ "$(head -n1 "$file" | tr -d '\r')" = "## Autopilot analysis" ]; then
    classes=$(grep -E '^Classification: ' "$file" | tr -d '\r' || true)
    if [ "$(grep -c . <<<"$classes")" -eq 1 ] && [[ $classes =~ ^Classification:\ (ready|needs-design|too-large|blocked)$ ]]; then
      class=${BASH_REMATCH[1]}
    fi
  fi
  [ -n "$class" ] || stopped analysis "The analysis produced no valid result. An analysis must start with \`## Autopilot analysis\` and name exactly one classification."
  body=$(defuse <"$file")
  if [ "$class" = ready ] && [ "$MODE" = full ] && [ "${AUTOPILOT_CLAUDE_AUTH:-subscription}" != api-key ]; then
    # The analysis run's own status: a build is the expensive part.
    claude_status "$WORK/usage-analysis.json"
    case $CLAUDE_STATUS in
      allowed_warning | rejected | overage)
        stopped analysis "$body

The issue was admitted, but nothing is built: $CLAUDE_STATUS_TEXT. Autopilot leaves the subscription to interactive use; add the label again later." ;;
    esac
  fi
  if [ "$class" = ready ] && [ "$MODE" = full ]; then
    say build - "$(with_usage "$body

**Admitted.** Building it now ([run]($RUN_URL)).")"
    board Build
    output build true
  elif [ "$class" = ready ]; then
    to_do - - "$(with_usage "$body

**Analysis only:** autopilot would admit this issue, but \`PIPELINE_MODE\` is \`analysis-only\`, so nothing is built. Back to To Do.")"
  else
    to_do "$class" - "$(with_usage "$body

**Not admitted ($class).** Back to To Do. [Run]($RUN_URL)")"
  fi
}

# --- decide: an investigation ---------------------------------------------------
# The report's sections (`### <name>`); a complete report has all of them.
REPORT_SECTIONS=("Summary" "Current implementation" "Findings" "Risks and dependencies"
  "Proposed implementation sequence" "Suggested follow-up work" "Open questions")

# conclude_investigation: the end of an investigation. Its only output is the report,
# posted on this issue, and with it the attempt ends: back to To Do, the issue stays
# open, `autopilot` is removed and `autopilot:analysis-only` stays. A result other
# than `complete` also gets its autopilot:<result> label, as an analysis does. Nothing
# here creates or closes an issue, or touches any other issue: suggested follow-up
# work stays text in the report. autopilot.test.sh checks that the whole script has
# no command that creates an issue, and that an investigation closes none (the only
# close is `outcome`'s, after a merge). A report that fails the checks is not posted
# as a report; the attempt stops, as on any other failure.
conclude_investigation() {
  local file="$WORK/analysis.md" result="" report missing s
  case ${PIPELINE_MODE:-off} in
    analysis-only | full) ;;
    *) stopped analysis "\`PIPELINE_MODE\` is now \`${PIPELINE_MODE:-off}\`, so the investigation report is not posted." ;;
  esac
  analysis_job_succeeded
  if [ -s "$file" ] && [ "$(head -n1 "$file" | tr -d '\r')" = "## Autopilot investigation" ] \
    && [[ $(sed -n 2p "$file" | tr -d '\r') =~ ^Result:\ (complete|needs-design|too-large|blocked)$ ]]; then
    result=${BASH_REMATCH[1]}
  fi
  [ -n "$result" ] || stopped analysis "The investigation produced no valid report. A report must start with \`## Autopilot investigation\` and a \`Result:\` line."
  report=$(tr -d '\r' <"$file" | defuse)

  if [ "$result" != complete ]; then
    to_do "$result" - "$(with_usage "$report

**Investigation not concluded ($result).** Back to To Do; this issue stays open. [Run]($RUN_URL)")"
    return 0
  fi

  missing=""
  for s in "${REPORT_SECTIONS[@]}"; do grep -qxF "### $s" <(tr -d '\r' <"$file") || missing+=" \`### $s\`"; done
  [ -z "$missing" ] || stopped analysis "The investigation report lacks the section(s)$missing."
  to_do - - "$(with_usage "$report

**Investigation complete.** This report is all autopilot does: it created no issues, and this issue stays open for the decisions. Follow-up work is only suggested above; a human opens the issues that are wanted. Back to To Do. [Run]($RUN_URL)")"
}

# --- publish ------------------------------------------------------------------
push_branch() {
  if [ -n "${AUTOPILOT_REMOTE:-}" ]; then git push -q "$AUTOPILOT_REMOTE" "$1:refs/heads/$2"; return; fi
  local auth
  auth=$(printf 'x-access-token:%s' "$AUTOPILOT_TOKEN" | base64 -w0)
  echo "::add-mask::$auth"
  git -c "http.https://github.com/.extraheader=AUTHORIZATION: basic $auth" push -q "https://github.com/$R.git" "$1:refs/heads/$2"
}

# Migrations that autopilot may not publish, one line each: an existing migration
# changed or removed (it may already have run in production), or an added one whose
# Up() drops, deletes or alters existing data. Irreversible data changes need a
# human (analysis.md, 2.6). A backstop for mistakes, not a parser: the data and
# understanding reviewers still read every migration.
unsafe_migrations() {
  local base=$1 head=$2 status path source up hits
  git diff --no-renames --name-status "$base" "$head" -- "$MIGRATIONS/" | while IFS=$'\t' read -r status path; do
    case $status:$path in
      M:"$MIGRATIONS"/*ModelSnapshot.cs) ;;
      A:*.Designer.cs) ;;
      A:*.cs)
        source=$(git show "$head:$path") || { echo "$path: could not be read"; continue; }
        up=$(awk '/void[[:space:]]+Up[[:space:]]*\(/ { up = 1 } /void[[:space:]]+Down[[:space:]]*\(/ { up = 0 } up' <<<"$source")
        # Changing an existing column can lose values, and a script can't tell a
        # safe change from a lossy one; SQL keywords in any case.
        hits=$( { grep -E '\.(DropTable|DropColumn|DropSchema|DeleteData|AlterColumn)[[:space:]]*(<[^>]*>)?[[:space:]]*\(' <<<"$up"
                  grep -iE '\b(DROP|TRUNCATE|DELETE)\b' <<<"$up"; } || true)
        [ -z "$hits" ] || echo "$path: Up() drops, deletes or alters existing data"
        ;;
      A:*) ;;
      *) echo "$path: an existing migration file is changed or removed ($status)" ;;
    esac
  done
}

cmd_publish() {
  need ISSUE GITHUB_RUN_ID; number ISSUE; ATTEMPT=$GITHUB_RUN_ID
  local result="$WORK/result.md" verdict body base head n guarded unsafe branch title url pr
  mkdir -p "$WORK"
  expect build || return 0
  has_label || stopped build "The autopilot label was removed during the build, so nothing was pushed."
  [ "${PIPELINE_MODE:-off}" = full ] || stopped build "\`PIPELINE_MODE\` is now \`${PIPELINE_MODE:-off}\`, not \`full\`, so nothing was pushed."
  [ "${BUILD_RESULT:-}" = success ] || stopped build "The build job did not succeed (${BUILD_RESULT:-unknown}): Claude crashed or ran out of time or turns. Nothing was pushed."
  [ -s "$result" ] || stopped build "Claude finished without writing a result. Nothing was pushed."
  verdict=$(head -n1 "$result" | tr -d '[:space:]')
  body=$(tail -n +2 "$result" | defuse)
  [ "$verdict" = READY ] || stopped build "Claude did not finish the implementation:

$body"
  [ "$(cat "$WORK/validation" 2>/dev/null)" = pass ] || stopped build "The validation after Claude's work failed: $(cat "$WORK/validation" 2>/dev/null || echo 'no result'). Nothing was pushed."
  base=$(cat "$WORK/base" 2>/dev/null || true)
  [[ $base =~ ^[0-9a-f]{40}$ ]] || stopped build "The build did not record the commit it started from. Nothing was pushed."
  git bundle verify -q "$WORK/autopilot.bundle" >/dev/null 2>&1 && git fetch -q "$WORK/autopilot.bundle" HEAD \
    || stopped build "Claude reported READY but left no commits to publish (or no readable bundle). Nothing was pushed."
  head=$(git rev-parse FETCH_HEAD)
  git merge-base --is-ancestor "$base" "$head" || stopped build "Claude's commits don't build on main ($base). Nothing was pushed."
  n=$(git rev-list --count "$base..$head")
  [ "$n" -gt 0 ] || stopped build "Claude reported READY but made no commits. Nothing was pushed."
  guarded=$(git diff --no-renames --name-only "$base" "$head" | grep -E "$GUARDED" || true)
  [ -z "$guarded" ] || stopped build "The change touches paths autopilot may not change (the review mechanism and the local guards):

\`\`\`
$guarded
\`\`\`

Nothing was pushed; this needs a human-authored PR."
  unsafe=$(unsafe_migrations "$base" "$head")
  [ -z "$unsafe" ] || stopped build "The change contains a migration autopilot may not publish (irreversible data changes and edits to existing migrations need a human):

\`\`\`
$unsafe
\`\`\`

Nothing was pushed; this needs a human-authored PR."

  branch="autopilot/issue-$ISSUE-r$ATTEMPT"
  push_branch "$head" "$branch" || stopped build "Pushing the branch failed. Nothing was opened."
  title=$(gh api "$API/issues/$ISSUE" --jq .title | tr -d '\r\n')
  printf '%s\n\nFixes #%s\n\nOpened by autopilot ([run](%s)) from `%s`.\n' "$body" "$ISSUE" "$RUN_URL" "$branch" >"$WORK/pr.md"
  # A draft first, so the state names the PR before any reviewer can finish.
  url=$(GH_TOKEN=$AUTOPILOT_TOKEN gh pr create --repo "$R" --base main --head "$branch" --draft \
    --title "$title (#$ISSUE)" --body-file "$WORK/pr.md") \
    || stopped build "The branch \`$branch\` was pushed, but opening the PR failed."
  pr=${url##*/}
  [[ $pr =~ ^[1-9][0-9]*$ ]] || stopped build "Opened a PR but could not read its number from '$url'."
  say merge "$pr" "$(with_usage "PR #$pr is open ($n commit(s) on \`$branch\`). The four reviewers and the build check run now; the merge gate decides.")"
  board Merge
  GH_TOKEN=$AUTOPILOT_TOKEN gh pr ready "$pr" --repo "$R" >/dev/null \
    || stopped merge "PR #$pr could not be marked ready for review, so the reviewers would never start." "$pr"
}

# --- outcome ------------------------------------------------------------------
# Runs at the end of every non-draft review run of an autopilot PR.
cmd_outcome() {
  need PR SHA HEAD_REF; number PR
  [[ $HEAD_REF =~ $BRANCH_RE ]] || { echo "Not an autopilot attempt branch: '$HEAD_REF'."; return 0; }
  ISSUE=${BASH_REMATCH[1]} ATTEMPT=${BASH_REMATCH[2]}
  local pull merged pstate head base msha verdicts build
  mkdir -p "$WORK"
  if ! active_attempt; then
    echo "PR #$PR is not the active attempt of issue #$ISSUE (state: $ACTIVE_STATE); nothing to do."
    return 0
  fi
  pull=$(gh api "$API/pulls/$PR" --jq '[.merged, .state, .head.sha, .base.ref, (.merge_commit_sha // "-")] | @tsv') \
    || fail "Could not read PR #$PR."
  read -r merged pstate head base msha <<<"$pull"
  if [ "$merged" = true ] && [ "$base" = main ]; then
    say done "$PR" "$(with_usage "**Done.** #$PR was merged into \`main\` ($msha).")"
    if [ "$(gh api "$API/issues/$ISSUE" --jq .state)" = open ]; then
      gh issue close "$ISSUE" --repo "$R" --reason completed >/dev/null || warn "Could not close issue #$ISSUE."
    fi
    drop_label
    board Done
    return 0
  fi
  if [ "$head" != "$SHA" ]; then echo "PR #$PR moved on to $head; the run for that commit decides."; return 0; fi
  if [ "${REVIEW_RESULT:-}" = cancelled ] || [ "${AUTOMERGE_RESULT:-}" = cancelled ]; then
    echo "This review run was cancelled; a newer run for PR #$PR decides."; return 0
  fi
  verdicts=$(gh api "$API/issues/$PR/comments" --paginate \
      --jq ".[] | $FROM_WORKFLOW | .body" 2>/dev/null \
    | grep -oE "<!-- claude-review: [a-z-]+ sha=$SHA blocking=[0-9]+ -->" \
    | sed -E 's/.*claude-review: ([a-z-]+) .*blocking=([0-9]+) -->/- \1: \2 blocking finding(s)/' | sort -u || true)
  if fixable "$pstate"; then
    # The marker first: from here no later review or rerun can grant another round,
    # and nothing merges until the fix moves the attempt back to merge.
    say fix "$PR" "$(with_usage "**The review blocked PR #$PR: one automatic fix round.** $FIX_SUMMARY for ${SHA:0:7}:
${verdicts:-- none found}

Claude now fixes only these findings, on the same PR. All four reviewers then review the new commit and the merge gate decides as usual. This is the attempt's only fix round: if the review blocks again, the attempt stops. [Review run]($RUN_URL)")"
    board Build
    gh workflow run autopilot-fix.yml --repo "$R" --ref main -f pr="$PR" -f sha="$SHA" >/dev/null \
      || stopped fix "The fix round could not be started (dispatching autopilot-fix.yml failed). Nothing was fixed." "$PR"
    return 0
  fi
  build=$(gh api "$API/commits/$SHA/check-runs?check_name=build&filter=latest&per_page=100" \
      --jq '.check_runs[] | select(.app.slug == "github-actions") | (.conclusion // .status)' 2>/dev/null | sort -u | tr '\n' ' ' || true)
  stopped merge "PR #$PR did not merge (${pstate:-unknown}). Reviewers: ${REVIEW_RESULT:-unknown}; merge gate: ${AUTOMERGE_RESULT:-unknown}; build check: ${build:-none found}.

Verdicts for ${SHA:0:7}:
${verdicts:-- none found}

No fix round: $FIX_REASON. See the review comments on the PR and the [review run]($RUN_URL)." "$PR"
}

# fixable <PR state>: may this review, which did not merge, get the attempt's one fix
# round? Only for findings a code change can address: every reviewer job ran and left
# a single well-formed verdict for $SHA (read by review-gate.sh, as the gate reads
# them), at least one of them blocks, and the understanding reviewer doesn't block on
# a decision (UNDOCUMENTED or CONTRADICTED: that needs Bas, not code). Never once the
# round is used, nor with anything a fix may not get past: a protected path,
# `no-automerge`, a mode other than full, the label gone. Says why not in FIX_REASON;
# FIX_SUMMARY counts the findings.
fixable() {
  local r v total=0 labels files understanding=0
  FIX_REASON="" FIX_SUMMARY=""
  if fix_round_used; then FIX_REASON="this attempt already had its one fix round"; return 1; fi
  [ "$1" = open ] || { FIX_REASON="the PR is ${1:-unknown}"; return 1; }
  [ "${REVIEW_RESULT:-}" = success ] || { FIX_REASON="not every reviewer job succeeded"; return 1; }
  [ "${AUTOMERGE_RESULT:-}" = failure ] || { FIX_REASON="the merge gate did not refuse it"; return 1; }
  [ "${PIPELINE_MODE:-off}" = full ] || { FIX_REASON="\`PIPELINE_MODE\` is \`${PIPELINE_MODE:-off}\`, not \`full\`"; return 1; }
  [[ $SHA =~ ^[0-9a-f]{40}$ ]] || { FIX_REASON="the reviewed commit is not a full SHA"; return 1; }
  has_label || { FIX_REASON="the issue no longer carries the autopilot label"; return 1; }
  labels=$(gh api "$API/issues/$PR/labels" --paginate --jq '.[].name') || { FIX_REASON="the PR's labels could not be read"; return 1; }
  ! grep -qx no-automerge <<<"$labels" || { FIX_REASON="the PR is labelled \`no-automerge\`"; return 1; }
  files=$(gh api "$API/pulls/$PR/files" --paginate --jq '.[] | .filename, (.previous_filename // empty)') \
    || { FIX_REASON="the PR's files could not be read"; return 1; }
  ! grep -qE "$GUARDED" <<<"$files" || { FIX_REASON="the PR changes paths autopilot may not change (the review mechanism and the local guards)"; return 1; }
  [ -n "${REVIEWERS// /}" ] || { FIX_REASON="REVIEWERS is not set"; return 1; }
  for r in $REVIEWERS; do
    v=$(bash "$(dirname "${BASH_SOURCE[0]}")/review-gate.sh" verdict "$r" 2>/dev/null) && [[ $v =~ blocking=([0-9]+)\ for ]] \
      || { FIX_REASON="$r left no single well-formed verdict for ${SHA:0:7}"; return 1; }
    total=$((total + BASH_REMATCH[1]))
    [ "$r" != understanding ] || understanding=${BASH_REMATCH[1]}
  done
  [ "$total" -gt 0 ] || { FIX_REASON="no reviewer reported a blocking finding, so the gate refused for another reason (see the run)"; return 1; }
  if [ "$understanding" -gt 0 ]; then
    v=$(gh api "$API/issues/$PR/comments" --paginate \
      --jq '.[] | '"$FROM_WORKFLOW"' | select(.body | contains("<!-- claude-review: understanding sha='"$SHA"' ")) | .body') \
      || { FIX_REASON="the understanding review could not be read"; return 1; }
    ! grep -qE '(UNDOCUMENTED|CONTRADICTED)' <<<"$v" \
      || { FIX_REASON="the understanding reviewer found a change the understanding doesn't support (UNDOCUMENTED or CONTRADICTED): that needs a decision, not a code fix"; return 1; }
  fi
  FIX_SUMMARY="$total blocking finding(s)"
}

# --- the fix round ------------------------------------------------------------
# .github/workflows/autopilot-fix.yml, dispatched by `outcome` with the PR and the
# commit its review blocked. Anyone with write access can dispatch it too, which
# changes nothing: every step goes on only while the attempt is in `fix` for that PR
# and the PR is still at that commit.

# fix_attempt: read PR $PR; its branch names the issue and attempt. Sets ISSUE,
# ATTEMPT, FIX_BRANCH and the PR as it is now (FIX_HEAD, FIX_PSTATE, FIX_BASE).
# Returns 1 (nothing to do) unless the attempt is in its fix round for this PR.
fix_attempt() {
  local pull ref
  need PR SHA GITHUB_RUN_ID; number PR
  [[ $SHA =~ ^[0-9a-f]{40}$ ]] || fail "SHA is not a full commit SHA: '$SHA'."
  pull=$(gh api "$API/pulls/$PR" --jq '[.head.ref, .head.sha, .state, .base.ref] | @tsv') || fail "Could not read PR #$PR."
  read -r ref FIX_HEAD FIX_PSTATE FIX_BASE <<<"$pull"
  [[ $ref =~ $BRANCH_RE ]] || { echo "PR #$PR is not an autopilot attempt ('$ref'); nothing to do."; return 1; }
  ISSUE=${BASH_REMATCH[1]} ATTEMPT=${BASH_REMATCH[2]} FIX_BRANCH=$ref
  expect_fix
}

# fix_admit: the fix round still applies to the PR as the review left it, and the
# Actions budget allows it. Anything else stops the attempt.
fix_admit() {
  has_label || stopped fix "The autopilot label was removed, so the fix round doesn't run." "$PR"
  [ "${PIPELINE_MODE:-off}" = full ] || stopped fix "\`PIPELINE_MODE\` is now \`${PIPELINE_MODE:-off}\`, not \`full\`, so the fix round doesn't run." "$PR"
  [ "$FIX_PSTATE" = open ] && [ "$FIX_BASE" = main ] || stopped fix "PR #$PR is $FIX_PSTATE (into $FIX_BASE), so the fix round doesn't run." "$PR"
  [ "$FIX_HEAD" = "$SHA" ] || stopped fix "PR #$PR moved on to ${FIX_HEAD:0:7} after the review of ${SHA:0:7}, so the fix round doesn't run." "$PR"
  actions_budget || stopped fix "The fix round doesn't run: $BUDGET_REASON." "$PR"
}

# fix-precheck: before the Claude probe, so that a refused round costs no Claude usage.
cmd_fix_precheck() {
  output ok false
  mkdir -p "$WORK"
  fix_attempt || return 0
  fix_admit
  output ok true
}

# fix-start: after the probe. Collects the findings for the fix step.
cmd_fix_start() {
  output go false
  mkdir -p "$WORK"
  fix_attempt || return 0
  fix_admit
  claude_gate "${CLAUDE_STATUS_FILE:-$WORK/claude-status.json}" || stopped fix "The fix round doesn't run: $CLAUDE_REASON." "$PR"
  collect_findings || stopped fix "The review findings for ${SHA:0:7} could not be read, so nothing was fixed." "$PR"
  echo "$BUDGET_SUMMARY"
  echo "$CLAUDE_SUMMARY"
  output go true
  output issue "$ISSUE"
}

# collect_findings: $WORK/findings.md, the review material for the fix step: the
# summaries of the reviewers that blocked $SHA and the inline comments on $SHA.
# Only the workflow's own comments (the reviewers post as github-actions[bot]).
collect_findings() {
  local summaries inline
  summaries=$(gh api "$API/issues/$PR/comments" --paginate \
    --jq '.[] | '"$FROM_WORKFLOW"' | select(.body | test("<!-- claude-review: [a-z-]+ sha='"$SHA"' blocking=[1-9][0-9]* -->")) | .body + "\n\n---\n"') || return 1
  [ -n "$summaries" ] || return 1
  inline=$(gh api "$API/pulls/$PR/comments" --paginate \
    --jq '.[] | '"$FROM_WORKFLOW"' | select(.commit_id == "'"$SHA"'" or .original_commit_id == "'"$SHA"'")
          | "### \(.path):\(.line // .original_line // "?")\n\n\(.body)\n\n---\n"') || return 1
  printf '# Blocking review findings on PR #%s at %s\n\n## Summaries of the reviewers that blocked\n\n%s\n\n## Inline comments\n\n%s\n' \
    "$PR" "$SHA" "$summaries" "${inline:-None.}" >"$WORK/findings.md"
}

# fix-publish: push Claude's fix to the PR's branch, after the same checks publish
# makes, over the whole PR as it would then be.
cmd_fix_publish() {
  local result="$WORK/result.md" verdict body base head n mb guarded unsafe
  mkdir -p "$WORK"
  fix_attempt || return 0
  has_label || stopped fix "The autopilot label was removed during the fix round, so nothing was pushed." "$PR"
  [ "${PIPELINE_MODE:-off}" = full ] || stopped fix "\`PIPELINE_MODE\` is now \`${PIPELINE_MODE:-off}\`, not \`full\`, so nothing was pushed." "$PR"
  [ "${FIX_RESULT:-}" = success ] || stopped fix "The fix job did not succeed (${FIX_RESULT:-unknown}): Claude crashed or ran out of time or turns. Nothing was pushed." "$PR"
  [ -s "$result" ] || stopped fix "Claude finished the fix round without writing a result. Nothing was pushed." "$PR"
  verdict=$(head -n1 "$result" | tr -d '[:space:]')
  body=$(tail -n +2 "$result" | defuse)
  [ "$verdict" = READY ] || stopped fix "Claude did not fix the findings:

$body" "$PR"
  [ "$(cat "$WORK/validation" 2>/dev/null)" = pass ] || stopped fix "The validation after Claude's fix failed: $(cat "$WORK/validation" 2>/dev/null || echo 'no result'). Nothing was pushed." "$PR"
  base=$(cat "$WORK/base" 2>/dev/null || true)
  [ "$base" = "$SHA" ] || stopped fix "The fix did not start from the reviewed commit ${SHA:0:7}. Nothing was pushed." "$PR"
  [ "$FIX_PSTATE" = open ] && [ "$FIX_HEAD" = "$SHA" ] || stopped fix "PR #$PR changed during the fix round (now $FIX_PSTATE at ${FIX_HEAD:0:7}). Nothing was pushed." "$PR"
  git bundle verify -q "$WORK/autopilot.bundle" >/dev/null 2>&1 && git fetch -q "$WORK/autopilot.bundle" HEAD \
    || stopped fix "Claude reported READY but left no commits to push (or no readable bundle). Nothing was pushed." "$PR"
  head=$(git rev-parse FETCH_HEAD)
  git merge-base --is-ancestor "$SHA" "$head" || stopped fix "Claude's commits don't build on ${SHA:0:7}. Nothing was pushed." "$PR"
  n=$(git rev-list --count "$SHA..$head")
  [ "$n" -gt 0 ] || stopped fix "Claude reported READY but made no commits. Nothing was pushed." "$PR"
  mb=$(git merge-base origin/main "$head") || stopped fix "Could not find where PR #$PR branches off main. Nothing was pushed." "$PR"
  guarded=$(git diff --no-renames --name-only "$mb" "$head" | grep -E "$GUARDED" || true)
  [ -z "$guarded" ] || stopped fix "The fix touches paths autopilot may not change (the review mechanism and the local guards):

\`\`\`
$guarded
\`\`\`

Nothing was pushed; this needs a human-authored PR." "$PR"
  unsafe=$(unsafe_migrations "$mb" "$head")
  [ -z "$unsafe" ] || stopped fix "The fix leaves a migration autopilot may not publish (irreversible data changes and edits to existing migrations need a human):

\`\`\`
$unsafe
\`\`\`

Nothing was pushed; this needs a human-authored PR." "$PR"
  # The marker first, so the review of the new commit finds the attempt in merge. A
  # failed push then stops the attempt; the old commit's review already blocked it.
  say merge "$PR" "$(with_usage "**Fix round pushed:** $n commit(s) on \`$FIX_BRANCH\`, on top of ${SHA:0:7}. All four reviewers and the build check run again on the new commit; the merge gate decides. This was the attempt's only fix round: if the review blocks again, the attempt stops. [Run]($RUN_URL)

$body")"
  push_branch "$head" "$FIX_BRANCH" \
    || stopped fix "Pushing the fix to \`$FIX_BRANCH\` failed (did the branch move?), so PR #$PR is unchanged." "$PR"
  board Merge
}

# --- gate ---------------------------------------------------------------------
# Called by review-gate.sh before it merges. Prints nothing useful for other PRs.
cmd_gate() {
  need PR; number PR
  local ref issue_state labels
  ref=$(gh api "$API/pulls/$PR" --jq '.head.ref // ""')
  [ -n "$ref" ] || fail "Could not read the head branch of PR #$PR."
  case $ref in autopilot/*) ;; *) return 0 ;; esac
  [[ $ref =~ $BRANCH_RE ]] || fail "Autopilot branch '$ref' doesn't name an issue and attempt."
  ISSUE=${BASH_REMATCH[1]} ATTEMPT=${BASH_REMATCH[2]}
  # The switch: with PIPELINE_MODE anything but full, no autopilot PR merges.
  [ "${PIPELINE_MODE:-off}" = full ] || fail "PIPELINE_MODE is '${PIPELINE_MODE:-off}', not 'full': autopilot PRs don't merge."
  issue_state=$(gh api "$API/issues/$ISSUE" --jq .state)
  [ "$issue_state" = open ] || fail "Autopilot issue #$ISSUE is $issue_state."
  labels=$(labels_of "$ISSUE")
  grep -qx autopilot <<<"$labels" || fail "Autopilot issue #$ISSUE no longer carries the autopilot label."
  active_attempt || fail "PR #$PR is not the active autopilot attempt of issue #$ISSUE (state: $ACTIVE_STATE)."
  echo "PR #$PR is the active autopilot attempt of issue #$ISSUE."
}

case "${1:-}" in
  precheck) cmd_precheck ;;
  start) cmd_start ;;
  decide) cmd_decide ;;
  publish) cmd_publish ;;
  outcome) cmd_outcome ;;
  fix-precheck) cmd_fix_precheck ;;
  fix-start) cmd_fix_start ;;
  fix-publish) cmd_fix_publish ;;
  gate) cmd_gate ;;
  *) fail "Usage: autopilot.sh precheck | start | decide | publish | outcome | fix-precheck | fix-start | fix-publish | gate" ;;
esac
