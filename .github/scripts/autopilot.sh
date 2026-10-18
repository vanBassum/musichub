#!/usr/bin/env bash
# The autopilot state machine: one attempt at one issue, started by the `autopilot`
# label (see .github/workflows/autopilot.yml and "Autopilot" in CLAUDE.md).
#
#   autopilot.sh precheck  the label was added: check who, the issue and the mode
#   autopilot.sh start     the same, plus Claude's usage status; then start the
#                          attempt (build, or for an investigation, analysis), wait
#                          for Claude's usage, or resume the previous attempt's PR
#   autopilot.sh decide    an investigation: post the report on the issue, To Do
#   autopilot.sh publish   act on Claude's build: push its commits and open the PR,
#                          or back to To Do with what Claude decided
#   autopilot.sh outcome   the review run of the PR ended: Done; a fix round; or
#                          back to To Do
#   autopilot.sh fix-precheck, fix-start, fix-publish
#                          a fix round (.github/workflows/autopilot-fix.yml): check
#                          it may run, collect the findings; push Claude's fix to the
#                          same PR for a full new review, or back to To Do
#   autopilot.sh gate      for review-gate.sh: refuse an autopilot PR that is not its
#                          issue's active attempt
#   autopilot.sh watchdog  a run of Autopilot, Autopilot fix or Claude PR review
#                          failed: retry its failed jobs once, then stop the attempt
#   autopilot.sh queue     start the issue that has waited longest for Claude's usage
#
# THE STATE is the newest marker comment the workflow (github-actions[bot]) left on
# the issue:   <!-- autopilot: issue=N run=R stage=S pr=P -->
# where R is the attempt's workflow run, S one of waiting, analysis, build, merge,
# fix, done, todo, and P the attempt's PR (or -). Every step first checks that the
# state is the one it expects, so a rerun, a late or a superseded run changes
# nothing. The merge gate merges an autopilot PR only while the state is `merge` for
# that PR and attempt and the issue still carries the label: writing any newer
# marker stops it.
#
# ONE STEP: the build job's Claude run judges the issue against the admission rules
# (.github/autopilot/admission.md) and the understanding, and builds it only when it
# may (.github/autopilot/build.md). What it decided is in the first line of its
# result: READY, or NEEDS-DESIGN, TOO-LARGE, BLOCKED. It runs in the sandbox
# (.github/scripts/sandbox.sh), never on the runner.
#
# FIX ROUNDS: when a review of the attempt's PR blocks it, `outcome` gives the
# attempt a fix round for what a code change can address: blocking reviewer
# findings, a failed build check, a conflict with main. The attempt moves to `fix`,
# the fix workflow pushes Claude's fix to the same PR and moves it back to `merge`,
# and all four reviewers and the gate judge the new commit afresh. At most
# MAX_FIX_ROUNDS per attempt (counted by its `fix` markers, which are never
# rewritten). Earlier than that, the attempt stops when
# - a finding needs a decision, not code: the understanding reviewer's
#   `decisions=` is above 0 (UNDOCUMENTED, CONTRADICTED, or not admissible);
# - a finding comes back that the previous round reported fixed: same key, in a
#   file that round changed (or the same failure of the build check);
# - Claude finds nothing left it can fix (BLOCKED);
# - Claude's usage status doesn't allow it.
# A new or different finding is no reason to stop: it gets the next round. While
# the state is `fix`, nothing merges.
#
# RETRIES: a job that fails (a crashed step, a timeout, a reviewer that left no
# verdict) fails its run; the watchdog (.github/workflows/autopilot-watchdog.yml)
# reruns the failed jobs once. The steps don't stop the attempt on the first
# failure of a job (GITHUB_RUN_ATTEMPT 1) for that reason, and the watchdog stops it
# when the retry fails too. What Claude decides is never retried.
#
# WAITING: when Claude's usage status doesn't allow a new attempt, the issue keeps
# its label and waits (stage `waiting`). The queue (.github/workflows/
# autopilot-queue.yml) starts it again later, through autopilot.yml's
# workflow_dispatch.
#
# RESUMING: adding the label again while the previous attempt's PR is still open
# resumes that attempt on that PR: main is merged into it (or the PR is marked ready
# again), it is reviewed afresh and gets MAX_FIX_ROUNDS new fix rounds. Nothing is
# rebuilt. Close the PR to start over instead.
#
# Text Claude wrote is posted with `<!--` defused, so it can't forge a marker.
#
# AN INVESTIGATION is an issue that also carries `autopilot:analysis-only`: the
# analysis step investigates and writes a report, and `decide` ends the attempt
# there (To Do -> Analysis -> To Do). The report is posted on the issue and that is
# all: nothing is built, no branch or PR is made, no issue is created and the issue
# stays open. Follow-up work appears only as suggestions in the report's text; a
# human decides which of it becomes an issue.
#
# Tokens: GH_TOKEN is the workflow's GITHUB_TOKEN (comments, labels, markers: they
# come from github-actions[bot] and trigger nothing). AUTOPILOT_TOKEN (the
# AUTOPILOT_PAT secret) pushes the branch, opens the PR and updates it when an
# attempt resumes: a branch, PR or push made with GITHUB_TOKEN starts no workflows
# (the reviewers and the build check would never run). AUTOPILOT_BOARD_TOKEN moves
# the board card: neither GITHUB_TOKEN nor a fine-grained token can reach a user's
# project.
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
# are not refused by path: the build admits them by risk, and the reviewers hold
# them to the understanding (see .github/autopilot/admission.md, 2.6).
GUARDED='^(\.github|\.githooks|\.claude)/'
# EF Core migrations live here once there are any; publish refuses the unsafe ones
# (unsafe_migrations).
MIGRATIONS=musichub-api/Data/Migrations
# The only comments trusted for state markers and verdicts: the workflow's own.
# A person, or Claude through a comment, can't post as github-actions[bot].
FROM_WORKFLOW='select(.user.login == "github-actions[bot]" and .user.type == "Bot")'
MAX_FIX_ROUNDS=3
# A finding's key: <reviewer>:<path>:<slug> as the reviewers mark each blocking
# finding (<!-- finding: KEY -->), or ci:<test|path>:<name|code> for a failure of the
# build check. Nothing in it can close an HTML comment.
KEY_RE='^[a-z][a-z-]*:[A-Za-z0-9_./@+-]+:[A-Za-z0-9_.-]+$'
export LC_ALL=C

need() { local v; for v in "$@"; do [ -n "${!v:-}" ] || fail "$v is not set."; done; }
number() { [[ ${!1:-} =~ ^[1-9][0-9]*$ ]] || fail "$1 is not a number: '${!1:-}'."; }
output() { echo "$1=$2" >>"${GITHUB_OUTPUT:-/dev/null}"; }
# Claude's text, made safe to post: no marker can be forged, and it fits in a
# comment or PR body (65536 characters).
defuse() { local t; t=$(sed 's/<!--/\&lt;!--/g'); printf '%s\n' "${t:0:60000}"; }
# retry_pending: this is the first attempt of the run, so a failed job gets the
# watchdog's one retry instead of stopping the attempt.
retry_pending() { [ "${GITHUB_RUN_ATTEMPT:-1}" -lt 2 ]; }

# --- reading ------------------------------------------------------------------
labels_of() { gh api "$API/issues/$1/labels" --paginate --jq '.[].name'; }
has_label() { local l; l=$(labels_of "$ISSUE") || fail "Could not read the labels of issue #$ISSUE."; grep -qx "${1:-autopilot}" <<<"$l"; }
INVESTIGATION_LABEL=autopilot:analysis-only
# On the issue: Claude's `allowed_warning` doesn't stop autopilot (rejected still does).
IGNORE_WARNING_LABEL=autopilot:ignore-claude-warning

# bot_bodies <issue>: the workflow's comments on issue <issue>, oldest first, one
# line each.
bot_bodies() {
  gh api "$API/issues/$1/comments" --paginate --jq '.[] | '"$FROM_WORKFLOW"' | .body | gsub("[\r\n]"; " ")' \
    || fail "Could not read the comments of issue #$1."
}

# The newest state marker on issue $1, as "run stage pr", or nothing. Callers
# capture it first (st=$(state_of N)) so that a failed API call stops the script.
state_of() {
  local bodies
  bodies=$(bot_bodies "$1") || exit 1
  { grep -oE "<!-- autopilot: issue=$1 run=[0-9]+ stage=[a-z]+ pr=([0-9]+|-) -->" <<<"$bodies" || true; } \
    | tail -n1 | sed -E 's/.* run=([0-9]+) stage=([a-z]+) pr=([0-9]+|-) -->/\1 \2 \3/'
}

# expect <stage>: carry on only if this attempt ($ATTEMPT) is in <stage>.
expect() {
  local st run stage pr
  st=$(state_of "$ISSUE") || fail "Could not read the state of issue #$ISSUE."
  read -r run stage pr <<<"$st" || true
  if [ "${run:-} ${stage:-}" != "$ATTEMPT $1" ]; then
    echo "Issue #$ISSUE is not in $1 for attempt $ATTEMPT (state: ${stage:-none}, run ${run:-none}); nothing to do."
    return 1
  fi
}

# expect_fix: carry on only if this attempt ($ATTEMPT) is in its fix round for $PR.
expect_fix() {
  local st run stage pr
  st=$(state_of "$ISSUE") || fail "Could not read the state of issue #$ISSUE."
  read -r run stage pr <<<"$st" || true
  if [ "${run:-} ${stage:-} ${pr:-}" != "$ATTEMPT fix $PR" ]; then
    echo "Issue #$ISSUE is not in its fix round for PR #$PR, attempt $ATTEMPT (state: ${stage:-none} ${pr:-} run ${run:-none}); nothing to do."
    return 1
  fi
}

# fix_rounds_used: how many fix rounds attempt $ATTEMPT of issue $ISSUE has had since
# it last resumed. Every `fix` marker counts, not only the newest: this is the
# budget, and markers are never rewritten.
fix_rounds_used() {
  local bodies
  bodies=$(bot_bodies "$ISSUE") || exit 1
  awk -v fix="<!-- autopilot: issue=$ISSUE run=$ATTEMPT stage=fix " -v resume="<!-- autopilot-resume: issue=$ISSUE run=$ATTEMPT " \
    'index($0, resume) { n = 0 } index($0, fix) { n++ } END { print n + 0 }' <<<"$bodies"
}

# active_attempt: is PR $PR the active attempt ($ATTEMPT) of issue $ISSUE, waiting
# in merge? The merge gate and the outcome report both ask exactly this. Leaves
# the state it found in ACTIVE_STATE for their messages.
active_attempt() {
  local st run stage pr
  st=$(state_of "$ISSUE") || fail "Could not read the state of issue #$ISSUE."
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
# AUTOPILOT_BOARD_TOKEN is a classic token with the `project` and `repo` scopes: a
# fine-grained token can't reach a project that belongs to a user, and `project`
# alone can't add an issue of a private repository. The node id comes from
# GITHUB_TOKEN.
board() {
  local n=${2:-$ISSUE} content
  if [ -z "${AUTOPILOT_BOARD_TOKEN:-}" ]; then warn "AUTOPILOT_BOARD_TOKEN is not set; the card of issue #$n stays where it is."; return 0; fi
  if [ -z "$PROJECT_ID" ]; then warn "AUTOPILOT_PROJECT_ID is not set; the card of issue #$n stays where it is."; return 0; fi
  if content=$(gh api "$API/issues/$n" --jq .node_id) && [ -n "$content" ]; then
    GH_TOKEN=$AUTOPILOT_BOARD_TOKEN board_move "$1" "$n" "$content" && return 0
  fi
  warn "Could not move issue #$n to '$1' on the board."
}
# board_move <status> <issue> <node id>, with the board token.
board_move() {
  local content=$3 item field option
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
  [ "$pr" = - ] || links="$links · PR #$pr (kept: add the \`autopilot\` label again to resume it, or close it to start over)"
  to_do blocked "$pr" "$(with_usage "**Autopilot stopped during $1; back to To Do.**

$2

$links")"
  exit 0
}

# --- budget: Claude ---------------------------------------------------------------
# The Claude subscription's session (5-hour) and weekly usage can't be read as a
# number: Claude Code reports it only as a status in its `rate_limit_event`, and
# raises it to `allowed_warning` (with the utilization) only when usage is high for
# that point in the window, on thresholds of Claude's own. Autopilot starts no new
# attempt and no fix round while Claude reports anything but `allowed`, or reports
# that usage runs on extra (overage) credit. A new attempt then waits (`waiting`).
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
# claude_gate <file>: before an attempt or a fix round, with the status of a fresh
# probe. Anything but an explicit `allowed` refuses (CLAUDE_REASON), except
# `allowed_warning` on an issue with $IGNORE_WARNING_LABEL. With
# AUTOPILOT_CLAUDE_AUTH `api-key`, build and fix don't use the subscription, so
# there is no status.
claude_gate() {
  CLAUDE_REASON="" CLAUDE_SUMMARY=""
  if [ "${AUTOPILOT_CLAUDE_AUTH:-subscription}" = api-key ]; then
    CLAUDE_SUMMARY="Claude: build and fix use the API key; its Console spend limit applies."
    return 0
  fi
  claude_status "${1:-}"
  case $CLAUDE_STATUS in
    allowed) CLAUDE_SUMMARY="$CLAUDE_STATUS_TEXT." ;;
    allowed_warning) if has_label "$IGNORE_WARNING_LABEL"; then CLAUDE_SUMMARY="$CLAUDE_STATUS_TEXT; ignored (\`$IGNORE_WARNING_LABEL\`)."; return 0; fi
      CLAUDE_REASON="$CLAUDE_STATUS_TEXT, so autopilot leaves the subscription to interactive use until it is back to \`allowed\` (or add \`$IGNORE_WARNING_LABEL\`)"; return 1 ;;
    rejected | overage) CLAUDE_REASON="$CLAUDE_STATUS_TEXT, so autopilot leaves the subscription to interactive use until it is back to \`allowed\`"; return 1 ;;
    *) CLAUDE_REASON="Claude's usage status could not be read (the probe run reported none)"; return 1 ;;
  esac
}

# usage_lines: one line per Claude run of this attempt, from the usage-*.json files
# the workflow extracted from the run's stream. These are client-side measurements
# and estimates, not what the subscription has left. A run whose permissions refused
# tool calls says so, with the calls: in an unattended run that means a needed tool
# is missing from its --allowedTools (issue #301).
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

# admit: the checks that cost nothing but API calls (who, which issue, the mode).
# Every job runs on the self-hosted runner, so there is no Actions budget. On a
# refusal it comments, removes the label and returns 1. From the queue, the label
# was added (and its author checked) when the attempt started waiting.
admit() {
  local mode=${PIPELINE_MODE:-off} issue_state
  has_label || { echo "The autopilot label is already gone; nothing to do."; return 1; }
  if [ "${TRIGGER:-label}" != queue ] && ! authorized "$SENDER"; then
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
}

# from_queue_ok <stage>: the queue starts only an issue that is waiting.
from_queue_ok() {
  [ "${TRIGGER:-label}" = queue ] || return 0
  if [ "${1:-}" != waiting ]; then
    echo "Issue #$ISSUE is not waiting (state: ${1:-none}); the queue starts nothing."
    return 1
  fi
}

# precheck: run before the Claude probe, so that a refused label costs no Claude
# usage at all. Outputs ok=true when the probe and `start` should run.
cmd_precheck() {
  need ISSUE SENDER GITHUB_RUN_ID; number ISSUE; ATTEMPT=$GITHUB_RUN_ID
  local st run stage pr
  output ok false
  mkdir -p "$WORK"
  st=$(state_of "$ISSUE") || fail "Could not read the state of issue #$ISSUE."
  read -r run stage pr <<<"$st" || true
  from_queue_ok "${stage:-}" || return 0
  admit || return 0
  output ok true
}

# resumable <stage> <pr> <run>: the previous attempt stopped with its PR still open,
# so adding the label again resumes it on that PR. Not when a person labelled the
# PR no-automerge (they hold it), nor outside full mode (nothing could merge).
resumable() {
  local pull ref state who
  [ "${1:-}" = todo ] && [[ ${2:-} =~ ^[1-9][0-9]*$ ]] && [ "${PIPELINE_MODE:-off}" = full ] || return 1
  pull=$(gh api "$API/pulls/$2" --jq '[.state, .head.ref] | @tsv') || return 1
  read -r state ref <<<"$pull"
  [ "$state" = open ] && [ "$ref" = "autopilot/issue-$ISSUE-r$3" ] || return 1
  who=$(gh api "$API/issues/$2/events" --paginate --jq '.[] | select(.event == "labeled" and .label.name == "no-automerge") | .actor.login' | tail -n1) || return 1
  [ -z "$who" ] || [ "$who" = "github-actions[bot]" ]
}

# resume <pr> <attempt>: the attempt is active again on its PR, which gets a new
# review: main is merged into it with the PAT (so the push starts the reviewers and
# the build check), or, when it is up to date, it is marked draft and ready again.
resume() {
  local pr=$1 by=$GITHUB_RUN_ID l
  ATTEMPT=$2
  say merge "$pr" "**Autopilot resumes attempt $ATTEMPT on PR #$pr** ([run]($RUN_URL)). Nothing is rebuilt: the PR gets the newest main and a full new review, and up to $MAX_FIX_ROUNDS new fix rounds. Close the PR and add the label again to start over instead.

<sub>$CLAUDE_SUMMARY</sub>

<!-- autopilot-resume: issue=$ISSUE run=$ATTEMPT by=$by -->"
  gh pr edit "$pr" --repo "$R" --remove-label no-automerge >/dev/null 2>&1 || true
  for l in needs-design too-large blocked; do
    gh issue edit "$ISSUE" --repo "$R" --remove-label "autopilot:$l" >/dev/null 2>&1 || true
  done
  board Merge
  if GH_TOKEN=$AUTOPILOT_TOKEN gh api -X PUT "$API/pulls/$pr/update-branch" >/dev/null 2>&1; then
    echo "Merged main into PR #$pr."
  elif GH_TOKEN=$AUTOPILOT_TOKEN gh pr ready "$pr" --repo "$R" --undo >/dev/null \
    && GH_TOKEN=$AUTOPILOT_TOKEN gh pr ready "$pr" --repo "$R" >/dev/null; then
    echo "PR #$pr is up to date (or conflicts with main); marked ready again for a new review."
  else
    stopped merge "PR #$pr could not be updated or marked ready again, so no new review starts." "$pr"
  fi
}

cmd_start() {
  need ISSUE SENDER GITHUB_RUN_ID; number ISSUE; ATTEMPT=$GITHUB_RUN_ID
  local mode=${PIPELINE_MODE:-off} st run stage pr previous="" labels l route started bodies
  output go false
  mkdir -p "$WORK"
  st=$(state_of "$ISSUE") || fail "Could not read the state of issue #$ISSUE."
  read -r run stage pr <<<"$st" || true
  # A rerun of this job (the watchdog retries a failed job once) goes on with the
  # attempt it already started; it never starts another.
  if [ "${run:-}" = "$ATTEMPT" ]; then
    case ${stage:-} in
      build) output go true; output route build; output mode "$mode"; output investigation false ;;
      analysis) output go true; output route investigation; output mode "$mode"; output investigation true ;;
    esac
    echo "This run already started attempt $ATTEMPT (state: ${stage:-none}); a rerun never starts another."
    return 0
  fi
  bodies=$(bot_bodies "$ISSUE") || fail "Could not read the comments of issue #$ISSUE."
  if grep -qE "<!-- autopilot-resume: issue=$ISSUE run=[0-9]+ by=$ATTEMPT -->" <<<"$bodies"; then
    echo "This run already resumed an attempt; a rerun does nothing."
    return 0
  fi
  from_queue_ok "${stage:-}" || return 0
  admit || return 0
  if ! claude_gate "${CLAUDE_STATUS_FILE:-$WORK/claude-status.json}"; then
    if [ "${stage:-}" = waiting ]; then
      echo "Still waiting: $CLAUDE_REASON."
      return 0
    fi
    say waiting - "**Autopilot is waiting:** $CLAUDE_REASON. The label stays: autopilot starts this issue by itself once Claude's status allows it (the queue checks every 30 minutes)."
    board "To Do"
    return 0
  fi
  echo "$CLAUDE_SUMMARY"

  labels=$(labels_of "$ISSUE") || fail "Could not read the labels of issue #$ISSUE."
  if ! grep -qx "$INVESTIGATION_LABEL" <<<"$labels" && resumable "${stage:-}" "${pr:-}" "${run:-}"; then
    resume "$pr" "$run"
    return 0
  fi
  case ${stage:-} in
    analysis | build) previous="

The previous attempt ([run]($SERVER/$R/actions/runs/$run)) ended without finishing; it is abandoned." ;;
    merge | fix) previous="

This supersedes the previous attempt: its PR #$pr can no longer merge automatically." ;;
  esac
  # The route is fixed here: `decide` and `publish` refuse to go on if the
  # investigation label changed since.
  if grep -qx "$INVESTIGATION_LABEL" <<<"$labels"; then
    route=investigation
    started="**Autopilot investigation started** (\`$INVESTIGATION_LABEL\`: a report on this issue, nothing else; no new issues, no branch, no PR, no code change; mode \`$mode\`, [run]($RUN_URL))."
    say analysis - "$started$previous

<sub>$CLAUDE_SUMMARY</sub>"
  else
    route=build
    if [ "$mode" = full ]; then
      started="**Autopilot attempt started** (mode \`full\`, [run]($RUN_URL)). In one run, Claude checks the issue against the understanding and, if it may be built, builds and tests it; then the PR opens."
    else
      started="**Autopilot analysis started** (mode \`$mode\`, [run]($RUN_URL)). Claude checks the issue against the understanding and says whether it would build it; nothing is built."
    fi
    say build - "$started$previous

<sub>$CLAUDE_SUMMARY</sub>"
  fi
  if [ "${stage:-}" = merge ] || [ "${stage:-}" = fix ]; then
    gh pr edit "$pr" --repo "$R" --add-label no-automerge >/dev/null || warn "Could not label PR #$pr no-automerge (it is superseded anyway)."
  fi
  for l in needs-design too-large blocked; do
    if grep -qx "autopilot:$l" <<<"$labels"; then gh issue edit "$ISSUE" --repo "$R" --remove-label "autopilot:$l" >/dev/null || true; fi
  done
  if [ "$route" = investigation ]; then board Analysis; else board Build; fi
  output go true
  output route "$route"
  output mode "$mode"
  output investigation "$([ "$route" = investigation ] && echo true || echo false)"
}

# --- decide: an investigation ---------------------------------------------------
# job_succeeded <job> <result> <stage> [pr]: unless the job as a whole succeeded,
# its output is not used, however complete it looks: a job that failed after Claude
# finished (a post step, the upload) is a pipeline failure, never a verdict. The
# first time, the watchdog retries it (returns 1, nothing changes); then the
# attempt stops.
job_succeeded() {
  [ "$2" = success ] && return 0
  if retry_pending; then
    echo "The $1 job did not succeed ($2); the watchdog retries it once."
    return 1
  fi
  stopped "$3" "The $1 job did not succeed, also when it was retried ($1 job: ${2:-unknown}), so its output is not used, even if it looks complete. This is a failure of the pipeline, not a verdict on the issue: see the run." "${4:--}"
}

cmd_decide() {
  need ISSUE GITHUB_RUN_ID; number ISSUE; ATTEMPT=$GITHUB_RUN_ID
  mkdir -p "$WORK"
  [ "${INVESTIGATION:-false}" = true ] || { echo "decide only concludes investigations."; return 0; }
  expect analysis || return 0
  has_label || stopped analysis "The autopilot label was removed during the analysis, so the attempt is stopped."
  has_label "$INVESTIGATION_LABEL" \
    || stopped analysis "The \`$INVESTIGATION_LABEL\` label was removed during the analysis, so this attempt no longer matches the issue. Nothing was built or created; add the \`autopilot\` label again to start over."
  conclude_investigation
}

# The report's sections (`### <name>`); a complete report has all of them.
REPORT_SECTIONS=("Summary" "Current implementation" "Findings" "Risks and dependencies"
  "Proposed implementation sequence" "Suggested follow-up work" "Open questions")

# conclude_investigation: the end of an investigation. Its only output is the report,
# posted on this issue, and with it the attempt ends: back to To Do, the issue stays
# open, `autopilot` is removed and `autopilot:analysis-only` stays. A result other
# than `complete` also gets its autopilot:<result> label. Nothing here creates or
# closes an issue, or touches any other issue: suggested follow-up work stays text
# in the report. autopilot.test.sh checks that the whole script has no command that
# creates an issue, and that an investigation closes none (the only close is
# `outcome`'s, after a merge). A report that fails the checks is not posted as a
# report; the attempt stops, as on any other failure.
conclude_investigation() {
  local file="$WORK/analysis.md" result="" report missing s
  case ${PIPELINE_MODE:-off} in
    analysis-only | full) ;;
    *) stopped analysis "\`PIPELINE_MODE\` is now \`${PIPELINE_MODE:-off}\`, so the investigation report is not posted." ;;
  esac
  job_succeeded analysis "${ANALYSIS_RESULT:-}" analysis || return 0
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
# human (admission.md, 2.6). A backstop for mistakes, not a parser: the api and
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

# validation_ok <stage> [pr]: what the pipeline's own check found before publishing
# (validate, in sandbox-job.sh). Uncommitted changes, or no result at all, stop the
# attempt: what Claude left is not what it reported. A failed build, test or lint is
# published anyway: the PR's build check fails the same way, and the review's fix
# round addresses it from the build check's results, as the understanding has
# implementation agents do (architecture, "Delivery pipeline"). Says so in
# VALIDATION_NOTE.
validation_ok() {
  local v
  v=$(tr -d '\r' <"$WORK/validation" 2>/dev/null || true)
  VALIDATION_NOTE=""
  case $v in
    pass) ;;
    "fail: Claude left uncommitted changes"*) stopped "$1" "The validation after Claude's work failed: $v. Nothing was pushed." "${2:--}" ;;
    fail:*) VALIDATION_NOTE="

The pipeline's own check before publishing failed (${v#fail: }). The build check on the PR will show the same failure, and a fix round takes it from there." ;;
    *) stopped "$1" "The validation after Claude's work left no result (${v:-none}). Nothing was pushed." "${2:--}" ;;
  esac
}

# claude_ended: a sentence on how Claude's run ended, for a stop without a result.
claude_ended() {
  local code
  code=$(tr -d '[:space:]' <"$WORK/claude-exit" 2>/dev/null || true)
  case $code in
    "") echo "Claude's run left no exit status." ;;
    0) echo "Claude's run ended normally." ;;
    *) echo "Claude's run ended with exit status $code (out of turns or time, or an error)." ;;
  esac
}

cmd_publish() {
  need ISSUE GITHUB_RUN_ID; number ISSUE; ATTEMPT=$GITHUB_RUN_ID
  local result="$WORK/result.md" verdict class body base head n guarded unsafe branch title url pr
  mkdir -p "$WORK"
  expect build || return 0
  has_label || stopped build "The autopilot label was removed during the build, so nothing was pushed."
  ! has_label "$INVESTIGATION_LABEL" \
    || stopped build "The \`$INVESTIGATION_LABEL\` label was added during the build, so this attempt no longer matches the issue. Nothing was pushed; add the \`autopilot\` label again to start over."
  if [ "${START_MODE:-}" = full ] && [ "${PIPELINE_MODE:-off}" != full ]; then
    stopped build "\`PIPELINE_MODE\` is now \`${PIPELINE_MODE:-off}\`, not \`full\`, so nothing was pushed."
  fi
  job_succeeded build "${BUILD_RESULT:-}" build || return 0
  [ -s "$result" ] || stopped build "Claude finished without writing a result. $(claude_ended) Nothing was pushed."
  verdict=$(head -n1 "$result" | tr -d '[:space:]')
  body=$(tail -n +2 "$result" | tr -d '\r' | defuse)
  case $verdict in
    READY) ;;
    NEEDS-DESIGN | TOO-LARGE | BLOCKED)
      class=$(tr 'A-Z' 'a-z' <<<"$verdict")
      to_do "$class" - "$(with_usage "$body

**Not built ($class).** Back to To Do. Answer what is asked above, then add the \`autopilot\` label again. [Run]($RUN_URL)")"
      return 0 ;;
    *) stopped build "Claude's result starts with neither READY nor NEEDS-DESIGN, TOO-LARGE or BLOCKED. Nothing was pushed." ;;
  esac
  if [ "${START_MODE:-}" != full ]; then
    to_do - - "$(with_usage "$body

**Analysis only:** autopilot would build this issue, but \`PIPELINE_MODE\` was \`${START_MODE:-unknown}\`, so nothing was built. Back to To Do. [Run]($RUN_URL)")"
    return 0
  fi
  validation_ok build
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
  say merge "$pr" "$(with_usage "PR #$pr is open ($n commit(s) on \`$branch\`). The four reviewers and the build check run now; the merge gate decides.$VALIDATION_NOTE")"
  board Merge
  GH_TOKEN=$AUTOPILOT_TOKEN gh pr ready "$pr" --repo "$R" >/dev/null \
    || stopped merge "PR #$pr could not be marked ready for review, so the reviewers would never start." "$pr"
}

# --- the review's findings -----------------------------------------------------------
# review_keys: the keys of the blocking findings the reviewers reported on $SHA, in
# their summaries and inline comments.
review_keys() {
  {
    gh api "$API/issues/$PR/comments" --paginate \
      --jq '.[] | '"$FROM_WORKFLOW"' | select(.body | contains("<!-- claude-review: ") and contains(" sha='"$SHA"' ")) | .body'
    gh api "$API/pulls/$PR/comments" --paginate \
      --jq '.[] | '"$FROM_WORKFLOW"' | select(.commit_id == "'"$SHA"'" or .original_commit_id == "'"$SHA"'") | .body'
  } | { grep -oE '<!-- finding: [^ ]+ -->' || true; } | sed -E 's/^<!-- finding: (.*) -->$/\1/' | { grep -E "$KEY_RE" || true; } | sort -u
}

# ci_failure: the newest run of the Build workflow (build.yml) for $SHA. When it
# failed, CI_RUN is its id and CI_JOBS its failed jobs ("<id><tab><name>" lines);
# both stay empty while it runs, when it passed or when there is none. Returns 1
# when it can't be read.
ci_failure() {
  local run id status conclusion
  CI_RUN="" CI_JOBS=""
  run=$(gh api "$API/actions/workflows/build.yml/runs?head_sha=$SHA&per_page=20" \
    --jq '[.workflow_runs[] | select(.event == "pull_request")] | sort_by(.run_number, .run_attempt) | last | select(. != null) | "\(.id) \(.status) \(.conclusion)"') || return 1
  [ -n "$run" ] || return 0
  read -r id status conclusion <<<"$run"
  [ "$status" = completed ] && [ "$conclusion" = failure ] || return 0
  CI_RUN=$id
  CI_JOBS=$(gh api "$API/actions/runs/$id/jobs?per_page=100" --jq '.jobs[] | select(.conclusion == "failure") | "\(.id)\t\(.name)"') || return 1
}

# ci_log <job id>: the job's log, without timestamps.
ci_log() {
  gh api "$API/actions/jobs/$1/logs" | tr -d '\r' | sed -E 's/^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9:.]+Z ?//'
}

# ci_keys: the keys of the failures in the failed jobs' logs (stdin): a failed .NET
# test (ci:test:<name>), a compiler error (ci:<path>:<code>), an ESLint error
# (ci:<path>:<rule>). A failure the logs don't name gets no key: it can't be
# recognised when it comes back, so it never stops the attempt early.
ci_keys() {
  awk '
    function norm(p) {
      if (match(p, /(musichub-api|musichub-ui)\//)) p = substr(p, RSTART)
      else if (p ~ /^src\//) p = "musichub-ui/" p
      return p
    }
    match($0, /^[[:space:]]*Failed [A-Za-z0-9_.]+/) {
      t = substr($0, RSTART, RLENGTH); sub(/^[[:space:]]*Failed /, "", t); print "ci:test:" t; next
    }
    # MSBuild and tsc: path(line,col): error CODE; tsc --pretty: path:line:col - error CODE
    match($0, /[A-Za-z0-9_.\/-]+\.(cs|tsx?|jsx?)(\([0-9]+,[0-9]+\):|:[0-9]+:[0-9]+ -) error [A-Z]+[0-9]+/) {
      s = substr($0, RSTART, RLENGTH); p = s; sub(/(\(|:[0-9]).*$/, "", p); c = s; sub(/^.* error /, "", c)
      print "ci:" norm(p) ":" c; next
    }
    /^[^[:space:]].*\.(tsx?|jsx?|mjs|cjs)$/ { file = norm($0); next }
    file != "" && /^[[:space:]]+[0-9]+:[0-9]+[[:space:]]+error[[:space:]]/ {
      rule = $NF; gsub(/@/, "", rule); gsub(/\//, ".", rule); print "ci:" file ":" rule
    }
  ' | { grep -E "$KEY_RE" || true; } | sort -u | head -n 30
}

# previous_fixed_keys: the keys the attempt's latest fix round reported fixed, in a
# file it changed or from the build check. They are in the comment with which that
# round moved the attempt back to merge.
previous_fixed_keys() {
  local bodies last
  bodies=$(bot_bodies "$ISSUE") || return 1
  last=$(grep -F "<!-- autopilot: issue=$ISSUE run=$ATTEMPT stage=merge pr=$PR -->" <<<"$bodies" | tail -n1 || true)
  { grep -oE "<!-- autopilot-fixed: key=[^ ]+ changed=1 -->" <<<"$last" || true; } | sed -E 's/^<!-- autopilot-fixed: key=([^ ]+) .*$/\1/' | sort -u
}

# --- outcome ------------------------------------------------------------------
# Runs at the end of every non-draft review run of an autopilot PR.
cmd_outcome() {
  need PR SHA HEAD_REF; number PR
  [[ $HEAD_REF =~ $BRANCH_RE ]] || { echo "Not an autopilot attempt branch: '$HEAD_REF'."; return 0; }
  ISSUE=${BASH_REMATCH[1]} ATTEMPT=${BASH_REMATCH[2]}
  local pull merged pstate head base msha mstate verdicts build round
  mkdir -p "$WORK"
  if ! active_attempt; then
    echo "PR #$PR is not the active attempt of issue #$ISSUE (state: $ACTIVE_STATE); nothing to do."
    return 0
  fi
  pull=$(gh api "$API/pulls/$PR" --jq '[.merged, .state, .head.sha, .base.ref, (.merge_commit_sha // "-"), (.mergeable_state // "unknown")] | @tsv') \
    || fail "Could not read PR #$PR."
  read -r merged pstate head base msha mstate <<<"$pull"
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
  if [ "${REVIEW_RESULT:-}" != success ] && retry_pending; then
    echo "A reviewer job did not succeed (${REVIEW_RESULT:-unknown}); the watchdog reruns it once, and that run decides."
    return 0
  fi
  MERGEABLE_STATE=$mstate
  verdicts=$(gh api "$API/issues/$PR/comments" --paginate \
      --jq ".[] | $FROM_WORKFLOW | .body" 2>/dev/null \
    | grep -oE "<!-- claude-review: [a-z-]+ sha=$SHA blocking=[0-9]+( decisions=[0-9]+)? -->" \
    | sed -E 's/.*claude-review: ([a-z-]+) .*blocking=([0-9]+).* -->/- \1: \2 blocking finding(s)/' | sort -u || true)
  if fixable "$pstate"; then
    round=$((FIX_ROUNDS + 1))
    # The marker first: from here no later review or rerun can grant another round,
    # and nothing merges until the fix moves the attempt back to merge.
    say fix "$PR" "$(with_usage "**The review blocked PR #$PR: fix round $round of $MAX_FIX_ROUNDS.** For ${SHA:0:7}: $FIX_SUMMARY.
${verdicts:-- no verdicts found}

Claude now fixes what a code change can fix, on the same PR. All four reviewers and the build check then judge the new commit and the merge gate decides as usual. [Review run]($RUN_URL)")"
    board Build
    gh workflow run autopilot-fix.yml --repo "$R" --ref main -f pr="$PR" -f sha="$SHA" >/dev/null \
      || stopped fix "The fix round could not be started (dispatching autopilot-fix.yml failed). Nothing was fixed." "$PR"
    return 0
  fi
  build=$(gh api "$API/commits/$SHA/check-runs?check_name=build&filter=latest&per_page=100" \
      --jq '.check_runs[] | select(.app.slug == "github-actions") | (.conclusion // .status)' 2>/dev/null | sort -u | tr '\n' ' ' || true)
  stopped merge "PR #$PR did not merge (${pstate:-unknown}). Reviewers: ${REVIEW_RESULT:-unknown}; merge gate: ${AUTOMERGE_RESULT:-unknown}${GATE_REASON:+ (\"$GATE_REASON\")}; build check: ${build:-none found}.

Verdicts for ${SHA:0:7}:
${verdicts:-- none found}

No fix round: $FIX_REASON. See the review comments on the PR and the [review run]($RUN_URL)." "$PR"
}

# fixable <PR state>: may this review, which did not merge, get a fix round? Only for
# what a code change can address: blocking reviewer findings, a failed build check
# or a conflict with main; every reviewer job ran and left a single well-formed
# verdict for $SHA (read by review-gate.sh, as the gate reads them), and none of the
# findings needs a decision (the understanding reviewer's decisions=). Never beyond
# MAX_FIX_ROUNDS, never when a finding the previous round reported fixed came back,
# nor with anything a fix may not get past: a protected path, `no-automerge`, a mode
# other than full, the label gone. Says why not in FIX_REASON; FIX_SUMMARY says what
# is to be fixed, FIX_ROUNDS how many rounds were used.
fixable() {
  local r v total=0 decisions=0 labels files what="" prev current repeated ci_failed=false
  FIX_REASON="" FIX_SUMMARY=""
  FIX_ROUNDS=$(fix_rounds_used) && [[ $FIX_ROUNDS =~ ^[0-9]+$ ]] || { FIX_REASON="the issue's comments could not be read"; return 1; }
  [ "$FIX_ROUNDS" -lt "$MAX_FIX_ROUNDS" ] || { FIX_REASON="this attempt already had its $MAX_FIX_ROUNDS fix rounds"; return 1; }
  [ "$1" = open ] || { FIX_REASON="the PR is ${1:-unknown}"; return 1; }
  [ "${REVIEW_RESULT:-}" = success ] || { FIX_REASON="not every reviewer job succeeded, also when it was retried"; return 1; }
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
    v=$(bash "$(dirname "${BASH_SOURCE[0]}")/review-gate.sh" verdict "$r" 2>/dev/null) && [[ $v =~ blocking=([0-9]+)\ decisions=([0-9]+)\ for ]] \
      || { FIX_REASON="$r left no single well-formed verdict for ${SHA:0:7}"; return 1; }
    total=$((total + BASH_REMATCH[1]))
    decisions=$((decisions + BASH_REMATCH[2]))
  done
  [ "$decisions" -eq 0 ] \
    || { FIX_REASON="$decisions finding(s) need a decision, not a code change (the understanding reviewer found the change UNDOCUMENTED, CONTRADICTED or not one autopilot may make on its own): that needs Bas"; return 1; }
  ci_failure || { FIX_REASON="the build check's runs could not be read"; return 1; }
  [ -z "$CI_RUN" ] || ci_failed=true
  [ "$total" -gt 0 ] && what="$total blocking finding(s)"
  [ "$ci_failed" = false ] || what="${what:+$what, }the build check failed ($(cut -f2 <<<"$CI_JOBS" | paste -sd, - | sed 's/,/, /g'))"
  [ "$MERGEABLE_STATE" != dirty ] || what="${what:+$what, }a conflict with main"
  [ -n "$what" ] || { FIX_REASON="no reviewer reported a blocking finding, the build check didn't fail and the PR doesn't conflict with main, so the gate refused for another reason${GATE_REASON:+: $GATE_REASON}"; return 1; }
  prev=$(previous_fixed_keys) || { FIX_REASON="the issue's comments could not be read"; return 1; }
  if [ -n "$prev" ]; then
    current=$( { review_keys; if [ "$ci_failed" = true ]; then while IFS=$'\t' read -r v _; do ci_log "$v" || true; done <<<"$CI_JOBS" | ci_keys; fi; } | sort -u)
    repeated=$(comm -12 <(printf '%s\n' "$prev") <(printf '%s\n' "$current") | grep . || true)
    [ -z "$repeated" ] \
      || { FIX_REASON="the previous fix round reported these findings fixed, and they came back: $(sed 's/^/`/; s/$/`/' <<<"$repeated" | paste -sd, - | sed 's/,/, /g'). Another round would most likely repeat it"; return 1; }
  fi
  FIX_SUMMARY=$what
}

# --- the fix round ------------------------------------------------------------
# .github/workflows/autopilot-fix.yml, dispatched by `outcome` with the PR and the
# commit its review blocked. Anyone with write access can dispatch it too, which
# changes nothing: every step goes on only while the attempt is in `fix` for that PR
# and the PR is still at that commit.

# fix_attempt: read PR $PR; its branch names the issue and attempt. Sets ISSUE,
# ATTEMPT, FIX_BRANCH and the PR as it is now (FIX_HEAD, FIX_PSTATE, FIX_BASE,
# FIX_MSTATE). Returns 1 (nothing to do) unless the attempt is in its fix round for
# this PR.
fix_attempt() {
  local pull ref
  need PR SHA GITHUB_RUN_ID; number PR
  [[ $SHA =~ ^[0-9a-f]{40}$ ]] || fail "SHA is not a full commit SHA: '$SHA'."
  pull=$(gh api "$API/pulls/$PR" --jq '[.head.ref, .head.sha, .state, .base.ref, (.mergeable_state // "unknown")] | @tsv') || fail "Could not read PR #$PR."
  read -r ref FIX_HEAD FIX_PSTATE FIX_BASE FIX_MSTATE <<<"$pull"
  [[ $ref =~ $BRANCH_RE ]] || { echo "PR #$PR is not an autopilot attempt ('$ref'); nothing to do."; return 1; }
  ISSUE=${BASH_REMATCH[1]} ATTEMPT=${BASH_REMATCH[2]} FIX_BRANCH=$ref
  expect_fix
}

# fix_admit: the fix round still applies to the PR as the review left it. Anything
# else stops the attempt.
fix_admit() {
  has_label || stopped fix "The autopilot label was removed, so the fix round doesn't run." "$PR"
  [ "${PIPELINE_MODE:-off}" = full ] || stopped fix "\`PIPELINE_MODE\` is now \`${PIPELINE_MODE:-off}\`, not \`full\`, so the fix round doesn't run." "$PR"
  [ "$FIX_PSTATE" = open ] && [ "$FIX_BASE" = main ] || stopped fix "PR #$PR is $FIX_PSTATE (into $FIX_BASE), so the fix round doesn't run." "$PR"
  [ "$FIX_HEAD" = "$SHA" ] || stopped fix "PR #$PR moved on to ${FIX_HEAD:0:7} after the review of ${SHA:0:7}, so the fix round doesn't run." "$PR"
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
  echo "$CLAUDE_SUMMARY"
  output go true
  output issue "$ISSUE"
  output round "$(fix_rounds_used)"
}

# collect_findings: $WORK/findings.md, what the fix step is to fix: the keys of the
# blocking findings, the summaries of the reviewers that blocked $SHA and their
# inline comments on it, the failed jobs of the build check with their logs, and a
# conflict with main. Only the workflow's own comments (the reviewers post as
# github-actions[bot]).
collect_findings() {
  local summaries inline keys ci="" id name log
  summaries=$(gh api "$API/issues/$PR/comments" --paginate \
    --jq '.[] | '"$FROM_WORKFLOW"' | select(.body | test("<!-- claude-review: [a-z-]+ sha='"$SHA"' blocking=[1-9][0-9]*( decisions=[0-9]+)? -->")) | .body + "\n\n---\n"') || return 1
  inline=$(gh api "$API/pulls/$PR/comments" --paginate \
    --jq '.[] | '"$FROM_WORKFLOW"' | select(.commit_id == "'"$SHA"'" or .original_commit_id == "'"$SHA"'")
          | "### \(.path):\(.line // .original_line // "?")\n\n\(.body)\n\n---\n"') || return 1
  keys=$(review_keys) || return 1
  ci_failure || return 1
  if [ -n "$CI_RUN" ]; then
    ci="The build check failed ([run]($SERVER/$R/actions/runs/$CI_RUN)). Its failed jobs, with the end of each log:
"
    while IFS=$'\t' read -r id name; do
      [ -n "$id" ] || continue
      log=$(ci_log "$id") || log="(the log could not be read)"
      keys=$(printf '%s\n%s\n' "$keys" "$(ci_keys <<<"$log")" | grep . | sort -u || true)
      ci+="
### Job: $name

Errors in the log:

\`\`\`text
$(grep -E 'error|Error|Failed|FAIL|✗|×' <<<"$log" | grep -v '^##\[' | head -n 60 || true)
\`\`\`

The end of the log:

\`\`\`text
$(tail -n 80 <<<"$log")
\`\`\`
"
    done <<<"$CI_JOBS"
  fi
  [ -n "$summaries" ] || [ -n "$ci" ] || [ "$FIX_MSTATE" = dirty ] || return 1
  {
    printf '# What to fix on PR #%s at %s\n\n' "$PR" "$SHA"
    printf '## Finding keys\n\nReport each one in result.md as `Fixed: <key>` or `Needs decision: <key>` (see fix.md).\n\n'
    if [ -n "$keys" ]; then sed 's/^/- /' <<<"$keys"; else echo "None (the findings below carry no key; describe what you fixed)."; fi
    if [ "$FIX_MSTATE" = dirty ]; then
      printf '\n## Conflict with main\n\nThe PR conflicts with main. The fix step merges main into the PR before you start; OUT_DIR/merge.md says how that went.\n'
    fi
    if [ -n "$ci" ]; then printf '\n## The build check\n\n%s\n' "$ci"; fi
    printf '\n## Summaries of the reviewers that blocked\n\n%s\n\n## Inline comments\n\n%s\n' "${summaries:-None.}" "${inline:-None.}"
  } >"$WORK/findings.md"
}

# fix-publish: push Claude's fix to the PR's branch, after the same checks publish
# makes, over the whole PR as it would then be.
cmd_fix_publish() {
  local result="$WORK/result.md" verdict body base head n mb guarded unsafe changed fixed key path c markers="" round
  mkdir -p "$WORK"
  fix_attempt || return 0
  has_label || stopped fix "The autopilot label was removed during the fix round, so nothing was pushed." "$PR"
  [ "${PIPELINE_MODE:-off}" = full ] || stopped fix "\`PIPELINE_MODE\` is now \`${PIPELINE_MODE:-off}\`, not \`full\`, so nothing was pushed." "$PR"
  job_succeeded fix "${FIX_RESULT:-}" fix "$PR" || return 0
  [ -s "$result" ] || stopped fix "Claude finished the fix round without writing a result. $(claude_ended) Nothing was pushed." "$PR"
  verdict=$(head -n1 "$result" | tr -d '[:space:]')
  body=$(tail -n +2 "$result" | tr -d '\r' | defuse)
  [ "$verdict" = READY ] || stopped fix "Claude found nothing it could fix without a decision:

$body" "$PR"
  validation_ok fix "$PR"
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
  # What this round reports fixed, for the next review: a finding that comes back
  # in a file this round changed (or a failure of the build check that comes back)
  # stops the attempt instead of getting another round.
  changed=$(git diff --name-only "$SHA" "$head")
  fixed=$(tr -d '\r' <"$result" | sed -n -E 's/^Fixed: *([^ ]+) *$/\1/p' | { grep -E "$KEY_RE" || true; } | sort -u)
  while IFS= read -r key; do
    [ -n "$key" ] || continue
    path=${key#*:}; path=${path%:*}; c=0
    if [ "${key%%:*}" = ci ] || grep -qxF -- "$path" <<<"$changed"; then c=1; fi
    markers+="<!-- autopilot-fixed: key=$key changed=$c -->"$'\n'
  done <<<"$fixed"
  round=$(fix_rounds_used)
  # The marker first, so the review of the new commit finds the attempt in merge. A
  # failed push then stops the attempt; the old commit's review already blocked it.
  say merge "$PR" "$(with_usage "**Fix round $round of $MAX_FIX_ROUNDS pushed:** $n commit(s) on \`$FIX_BRANCH\`, on top of ${SHA:0:7}. All four reviewers and the build check run again on the new commit; the merge gate decides. [Run]($RUN_URL)$VALIDATION_NOTE

$body")
$markers"
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

# --- watchdog -------------------------------------------------------------------
# .github/workflows/autopilot-watchdog.yml, when a run of Autopilot, Autopilot fix or
# Claude PR review failed (WATCHED_RUN, WATCHED_ATTEMPT, WATCHED_WORKFLOW,
# WATCHED_TITLE: the run's name, which names the issue or PR). A failed job is a
# crash, a timeout or a reviewer that left no verdict, never something Claude
# decided: those end in a comment, not a failed job. The first time, the failed jobs
# are rerun; when the rerun fails too, an attempt that is still waiting for that
# run stops with the jobs that failed. A review run whose only failure is the merge
# gate's refusal is no failure here: `outcome` handles that.
cmd_watchdog() {
  need WATCHED_RUN WATCHED_ATTEMPT WATCHED_WORKFLOW WATCHED_TITLE
  local failed jobs st run stage pr stage_want="" ref watched_url active=false retry=false
  number WATCHED_RUN; number WATCHED_ATTEMPT
  mkdir -p "$WORK"
  watched_url="$SERVER/$R/actions/runs/$WATCHED_RUN"
  failed=$(gh api "$API/actions/runs/$WATCHED_RUN/attempts/$WATCHED_ATTEMPT/jobs?per_page=100" --paginate \
    --jq '.jobs[] | select(.conclusion == "failure") | .name') || fail "Could not read the jobs of run $WATCHED_RUN."
  [ -n "$failed" ] || { echo "No job of run $WATCHED_RUN failed."; return 0; }
  jobs=$(sed 's/^/`/; s/$/`/' <<<"$failed" | paste -sd, - | sed 's/,/, /g')
  ISSUE="" PR=""
  case $WATCHED_WORKFLOW in
    Autopilot)
      [[ $WATCHED_TITLE =~ ^Autopilot\ \#([1-9][0-9]*)$ ]] || { echo "Not an autopilot run: '$WATCHED_TITLE'."; return 0; }
      ISSUE=${BASH_REMATCH[1]} ATTEMPT=$WATCHED_RUN
      st=$(state_of "$ISSUE") || fail "Could not read the state of issue #$ISSUE."; read -r run stage pr <<<"$st" || true
      if [ "${run:-}" = "$ATTEMPT" ] && [[ ${stage:-} =~ ^(analysis|build)$ ]]; then active=true stage_want=$stage; fi
      # A start that failed before it recorded anything is retried as well.
      retry=$active; ! grep -qx start <<<"$failed" || retry=true ;;
    "Autopilot fix")
      [[ $WATCHED_TITLE =~ ^Autopilot\ fix\ for\ PR\ \#([1-9][0-9]*)$ ]] || { echo "Not a fix run: '$WATCHED_TITLE'."; return 0; }
      PR=${BASH_REMATCH[1]}
      ref=$(gh api "$API/pulls/$PR" --jq .head.ref) || fail "Could not read PR #$PR."
      [[ $ref =~ $BRANCH_RE ]] || { echo "PR #$PR is not an autopilot attempt."; return 0; }
      ISSUE=${BASH_REMATCH[1]} ATTEMPT=${BASH_REMATCH[2]}
      st=$(state_of "$ISSUE") || fail "Could not read the state of issue #$ISSUE."; read -r run stage pr <<<"$st" || true
      if [ "${run:-} ${stage:-} ${pr:-}" = "$ATTEMPT fix $PR" ]; then active=true stage_want=fix; fi
      retry=$active ;;
    "Claude PR review")
      [[ $WATCHED_TITLE =~ ^Claude\ PR\ review\ \#([1-9][0-9]*)$ ]] || { echo "Not a review run: '$WATCHED_TITLE'."; return 0; }
      PR=${BASH_REMATCH[1]}
      grep -qE '^(review \(.*\)|autopilot)$' <<<"$failed" \
        || { echo "Only the merge gate refused in run $WATCHED_RUN; nothing to retry."; return 0; }
      retry=true
      ref=$(gh api "$API/pulls/$PR" --jq .head.ref) || fail "Could not read PR #$PR."
      if [[ $ref =~ $BRANCH_RE ]]; then
        ISSUE=${BASH_REMATCH[1]} ATTEMPT=${BASH_REMATCH[2]}
        if active_attempt; then active=true stage_want=merge; fi
      fi ;;
    *) echo "Not a watched workflow: '$WATCHED_WORKFLOW'."; return 0 ;;
  esac
  RUN_URL=$watched_url
  if [ "$WATCHED_ATTEMPT" -lt 2 ]; then
    [ "$retry" = true ] || { echo "Run $WATCHED_RUN belongs to no active attempt; nothing to retry."; return 0; }
    # With the PAT when there is one: a rerun started with GITHUB_TOKEN may not
    # start this watchdog again when it fails too.
    if { [ -n "${AUTOPILOT_TOKEN:-}" ] && GH_TOKEN=$AUTOPILOT_TOKEN gh run rerun "$WATCHED_RUN" --failed --repo "$R" >/dev/null; } \
      || gh run rerun "$WATCHED_RUN" --failed --repo "$R" >/dev/null; then
      echo "Rerunning the failed jobs of run $WATCHED_RUN: $jobs."
      [ "$active" = false ] || note "These jobs failed in [run $WATCHED_RUN]($watched_url): $jobs. Autopilot reruns them once."
      return 0
    fi
    [ "$active" = true ] || fail "Could not rerun the failed jobs of run $WATCHED_RUN."
    stopped "$stage_want" "These jobs failed in [run $WATCHED_RUN]($watched_url), and rerunning them could not be started: $jobs." "${PR:--}"
  fi
  [ "$active" = true ] || { echo "Run $WATCHED_RUN failed again; no active attempt waits for it, so there is nothing to stop."; return 0; }
  stopped "$stage_want" "These jobs failed in [run $WATCHED_RUN]($watched_url), also when they were rerun: $jobs. That is a failure of the pipeline, not a verdict on the issue: see the run." "${PR:--}"
}

# --- queue --------------------------------------------------------------------
# .github/workflows/autopilot-queue.yml, every 30 minutes: start the waiting issue
# with the lowest number, through autopilot.yml's workflow_dispatch. One at a time:
# its start probes Claude's usage itself, and the next tick takes the next one.
cmd_queue() {
  local n st run stage pr
  for n in $(gh api "$API/issues?labels=autopilot&state=open&per_page=100" --paginate \
      --jq '.[] | select(.pull_request == null) | .number' | sort -n); do
    st=$(state_of "$n") || fail "Could not read the state of issue #$n."; read -r run stage pr <<<"$st" || true
    [ "${stage:-}" = waiting ] || continue
    gh workflow run autopilot.yml --repo "$R" --ref main -f issue="$n" >/dev/null || fail "Could not start autopilot for issue #$n."
    echo "Started autopilot again for issue #$n, which was waiting."
    return 0
  done
  echo "No issue is waiting."
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
  watchdog) cmd_watchdog ;;
  queue) cmd_queue ;;
  *) fail "Usage: autopilot.sh precheck | start | decide | publish | outcome | fix-precheck | fix-start | fix-publish | gate | watchdog | queue" ;;
esac
