#!/usr/bin/env bash
# Tests for review-gate.sh against a fake `gh` that serves canned API responses.
# Each case builds a scenario, runs the gate and checks whether it merged.
# Needs bash and jq. Run: bash .github/scripts/review-gate.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
GATE="$HERE/review-gate.sh"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

SHA=1111111111111111111111111111111111111111
OLD=2222222222222222222222222222222222222222
REVIEWERS="api ui understanding smells"

# --- fake gh -----------------------------------------------------------------
# `gh api <endpoint> [--paginate] --jq <filter>` prints jq <filter> over
# $SCENARIO/<name>.json, where <name> is the endpoint after repos/o/r/ with '/'
# turned into '_' and the query dropped. A <name>.fail file makes the call fail.
# `gh pr merge` records the token it was given and its arguments in $SCENARIO/merged.
mkdir -p "$WORK/bin"
cat >"$WORK/bin/gh" <<'EOF'
#!/usr/bin/env bash
if [ "$1 $2" = "pr merge" ]; then echo "[$GH_TOKEN] $*" >"$SCENARIO/merged"; exit 0; fi
[ "$1" = api ] || { echo "fake gh: unsupported: $*" >&2; exit 2; }
endpoint=$2; filter=.
shift 2
while [ $# -gt 0 ]; do
  case $1 in --jq) filter=$2; shift 2 ;; *) shift ;; esac
done
name=${endpoint%%\?*}; name=${name#repos/*/*/}; name=${name//\//_}
name=$(sed -E 's/^commits_[0-9a-f]+_/commits_/' <<<"$name")
[ -e "$SCENARIO/$name.fail" ] && { echo "HTTP 502: Bad Gateway" >&2; exit 1; }
[ -e "$SCENARIO/$name.json" ] || { echo "HTTP 404: Not Found ($endpoint)" >&2; exit 1; }
jq -r "$filter" "$SCENARIO/$name.json" | tr -d '\r'
EOF
chmod +x "$WORK/bin/gh"
export PATH="$WORK/bin:$PATH"

# --- scenario helpers ---------------------------------------------------------
marker() { echo "<!-- claude-review: $1 sha=${3:-$SHA} blocking=$2 -->"; }

# new_scenario: a PR that should merge. Cases then break one thing.
new_scenario() {
  SCENARIO="$WORK/$1"; export SCENARIO
  mkdir -p "$SCENARIO"
  echo "{\"state\":\"open\",\"draft\":false,\"head\":{\"sha\":\"$SHA\",\"ref\":\"feature/x\"},\"base\":{\"ref\":\"main\"},\"changed_files\":1}" >"$SCENARIO/pulls_7.json"
  echo '[{"filename":"musichub-api/Services/Domain/DocumentService.cs"}]' >"$SCENARIO/pulls_7_files.json"
  echo '[]' >"$SCENARIO/issues_7_labels.json"
  build success
  echo '[]' >"$SCENARIO/issues_7_comments.json"
  for r in $REVIEWERS; do comment "### Claude review — $r
No findings.
$(marker "$r" 0)"; done
}
edit_comments() {
  jq "$@" "$SCENARIO/issues_7_comments.json" >"$SCENARIO/c.tmp" && mv "$SCENARIO/c.tmp" "$SCENARIO/issues_7_comments.json"
}
# comment <body> [login] [type]: add an issue comment, by default from the bot.
comment() {
  edit_comments --arg b "$1" --arg l "${2:-github-actions[bot]}" --arg t "${3:-Bot}" \
    '. + [{body: $b, user: {login: $l, type: $t}}]'
}
# drop_comments <reviewer>: remove that reviewer's summary.
drop_comments() {
  edit_comments --arg r "$1" '[.[] | select(.body | contains("claude-review: \($r) ") | not)]'
}
# build <conclusion|pending|none>
build() {
  case $1 in
    none) echo '{"check_runs":[]}' ;;
    pending) echo '{"check_runs":[{"status":"in_progress","conclusion":null,"app":{"slug":"github-actions"}}]}' ;;
    *) echo "{\"check_runs\":[{\"status\":\"completed\",\"conclusion\":\"$1\",\"app\":{\"slug\":\"github-actions\"}}]}" ;;
  esac >"$SCENARIO/commits_check-runs.json"
}
pr_field() { jq "$1" "$SCENARIO/pulls_7.json" >"$SCENARIO/p.tmp" && mv "$SCENARIO/p.tmp" "$SCENARIO/pulls_7.json"; }

# --- assertions ---------------------------------------------------------------
PASS=0; FAILED=0
run_gate() {
  GITHUB_REPOSITORY=vanBassum/musichub PR=7 SHA=$SHA REVIEWERS=$REVIEWERS PIPELINE_MODE=${PIPELINE_MODE:-full}     GH_TOKEN=gha MERGE_TOKEN=${MERGE_TOKEN-app} \
    BUILD_WAIT_SECONDS=${BUILD_WAIT_SECONDS:-0} POLL_SECONDS=0 \
    bash "$GATE" "$@" >"$SCENARIO/out" 2>&1
}
expect_merge() {
  if run_gate merge && [ -e "$SCENARIO/merged" ]; then
    PASS=$((PASS + 1)); echo "ok    $1"
  else
    FAILED=$((FAILED + 1)); echo "FAIL  $1 (expected a merge)"; sed 's/^/      /' "$SCENARIO/out"
  fi
}
expect_refusal() {
  local want=$2
  if run_gate merge || [ -e "$SCENARIO/merged" ]; then
    FAILED=$((FAILED + 1)); echo "FAIL  $1 (merged, expected a refusal)"; sed 's/^/      /' "$SCENARIO/out"
  elif ! grep -qF -- "$want" "$SCENARIO/out"; then
    FAILED=$((FAILED + 1)); echo "FAIL  $1 (refused, but not with '$want')"; sed 's/^/      /' "$SCENARIO/out"
  else
    PASS=$((PASS + 1)); echo "ok    $1"
  fi
}

# --- cases --------------------------------------------------------------------
new_scenario happy
expect_merge "all reviewers blocking=0 and build green: merges"
grep -q -- "--match-head-commit $SHA" "$SCENARIO/merged" \
  && { PASS=$((PASS + 1)); echo "ok    merge is pinned to the reviewed head SHA"; } \
  || { FAILED=$((FAILED + 1)); echo "FAIL  merge is not pinned to the reviewed head SHA"; }
grep -q -- "^\[app\] pr merge" "$SCENARIO/merged" \
  && { PASS=$((PASS + 1)); echo "ok    merges with the PAT, so the push starts CI/CD"; } \
  || { FAILED=$((FAILED + 1)); echo "FAIL  does not merge with the PAT: $(cat "$SCENARIO/merged")"; }

# The merge of #292 was made with GITHUB_TOKEN and started no CI/CD: never again.
new_scenario no-merge-token
MERGE_TOKEN= expect_refusal "no MERGE_TOKEN: refuses instead of merging without CI/CD" "No MERGE_TOKEN"

new_scenario rerun-same
comment "$(marker api 0)"
expect_merge "a repeated identical verdict (rerun) still merges"

new_scenario missing
drop_comments smells
expect_refusal "a reviewer without a verdict refuses" "No verdict from smells"

new_scenario blocking
drop_comments api; comment "### Claude review — api
- [bug] something
$(marker api 2)"
expect_refusal "a blocking finding refuses" "api reported 2 blocking"

new_scenario stale
drop_comments understanding; comment "$(marker understanding 0 "$OLD")"
expect_refusal "a verdict for an older commit doesn't count" "No verdict from understanding"

new_scenario malformed
drop_comments ui; comment "<!-- claude-review: ui sha=$SHA blocking=none -->"
expect_refusal "a malformed verdict refuses" "Malformed verdict from ui"

new_scenario short-sha
drop_comments ui; comment "<!-- claude-review: ui sha=${SHA:0:7} blocking=0 -->"
expect_refusal "a verdict with a short SHA doesn't count" "No verdict from ui"

new_scenario newer-pass
drop_comments smells
comment "$(marker smells 1)"
comment "$(marker smells 0)"
expect_merge "newer approval replaces old blocking verdict on same SHA"

new_scenario newer-block
comment "$(marker smells 1)"
expect_refusal "newer blocking verdict supersedes old approval" "smells reported 1 blocking"

new_scenario newer-malformed
comment "<!-- claude-review: smells sha=$SHA blocking=none -->"
expect_refusal "malformed newest verdict refuses" "Malformed verdict from smells"

new_scenario forged-by-user
drop_comments api; comment "$(marker api 0)" vanBassum User
expect_refusal "a verdict posted by a person doesn't count" "No verdict from api"

new_scenario workflow-change
echo '[{"filename":".github/workflows/claude-review.yml"}]' >"$SCENARIO/pulls_7_files.json"
expect_refusal "a workflow change never merges itself" "Bas merges it by hand"

new_scenario reviewer-rule-change
echo '[{"filename":"README.md"},{"filename":".github/reviewers/api.md"}]' >"$SCENARIO/pulls_7_files.json"
pr_field '.changed_files = 2'
expect_refusal "a reviewer-rule change never merges itself" "Bas merges it by hand"

new_scenario rename-out-of-github
echo '[{"filename":"docs/gate.sh","previous_filename":".github/scripts/review-gate.sh"}]' >"$SCENARIO/pulls_7_files.json"
expect_refusal "moving a file out of .github/ never merges itself" "Bas merges it by hand"

new_scenario githooks-change
echo '[{"filename":".githooks/pre-push"}]' >"$SCENARIO/pulls_7_files.json"
expect_refusal "a change to the git hooks never merges itself" "Bas merges it by hand"

new_scenario claude-guard-change
echo '[{"filename":".claude/hooks/guard.js"}]' >"$SCENARIO/pulls_7_files.json"
expect_refusal "a change to the Claude guard never merges itself" "Bas merges it by hand"

new_scenario similar-name
echo '[{"filename":"docs/.github-notes.md"},{"filename":"musichub-api/.claude-ish.txt"}]' >"$SCENARIO/pulls_7_files.json"
pr_field '.changed_files = 2'
expect_merge "paths that only look like guarded ones still merge"

new_scenario incomplete-file-list
pr_field '.changed_files = 3001'
expect_refusal "an incomplete file list refuses" "can't check them all"

new_scenario build-failed
build failure
expect_refusal "a failed build refuses" "did not pass"

new_scenario build-pending
build pending
expect_refusal "a build that doesn't finish in time refuses" "did not finish in time"

new_scenario build-missing
build none
expect_refusal "a missing build refuses" "no run found"

new_scenario no-automerge
echo '[{"name":"no-automerge"}]' >"$SCENARIO/issues_7_labels.json"
expect_refusal "the no-automerge label refuses" "no-automerge"

new_scenario head-moved
pr_field ".head.sha = \"$OLD\""
expect_refusal "a new commit after the review refuses" "PR head moved"

new_scenario draft
pr_field '.draft = true'
expect_refusal "a draft refuses" "PR is a draft"

new_scenario closed
pr_field '.state = "closed"'
expect_refusal "a closed PR refuses" "PR is closed"

new_scenario wrong-base
pr_field '.base.ref = "release"'
expect_refusal "a PR into another branch refuses" "PR targets release"

new_scenario api-error-comments
touch "$SCENARIO/issues_7_comments.fail"
expect_refusal "an API error reading verdicts refuses" "502"

new_scenario api-error-files
touch "$SCENARIO/pulls_7_files.fail"
expect_refusal "an API error listing files refuses" "502"

new_scenario api-error-labels
touch "$SCENARIO/issues_7_labels.fail"
expect_refusal "an API error reading labels refuses" "502"

new_scenario api-error-checks
touch "$SCENARIO/commits_check-runs.fail"
expect_refusal "an API error reading checks refuses" "502"

new_scenario no-reviewers
REVIEWERS=" " expect_refusal "an empty reviewer list refuses" "REVIEWERS is empty"

# --- autopilot PRs (branch autopilot/issue-N-rRUN) -----------------------------
# autopilot <stage> [pr] [run]: PR 7 comes from autopilot/issue-12-r555, and issue
# 12's newest state marker says <stage>.
autopilot() {
  pr_field '.head.ref = "autopilot/issue-12-r555"'
  echo '{"state":"open"}' >"$SCENARIO/issues_12.json"
  echo '[{"name":"autopilot"}]' >"$SCENARIO/issues_12_labels.json"
  jq -n --arg b "Started. <!-- autopilot: issue=12 run=555 stage=analysis pr=- -->" \
    --arg c "PR open. <!-- autopilot: issue=12 run=${3:-555} stage=$1 pr=${2:-7} -->" \
    '[{body: $b, user: {login: "github-actions[bot]", type: "Bot"}}, {body: $c, user: {login: "github-actions[bot]", type: "Bot"}}]' \
    >"$SCENARIO/issues_12_comments.json"
}

new_scenario autopilot-active
autopilot merge
expect_merge "the active autopilot attempt merges"

new_scenario autopilot-switched-off
autopilot merge
PIPELINE_MODE=analysis-only expect_refusal "no autopilot PR merges unless PIPELINE_MODE is full" "not 'full'"

new_scenario human-pr-mode-off
PIPELINE_MODE=off expect_merge "PIPELINE_MODE doesn't affect other PRs"

new_scenario autopilot-stopped
autopilot todo
expect_refusal "a stopped autopilot attempt never merges" "not the active autopilot attempt"

new_scenario autopilot-in-fix-round
autopilot fix
expect_refusal "nothing merges while the attempt is in its fix round" "not the active autopilot attempt"

new_scenario autopilot-other-pr
autopilot merge 8
expect_refusal "an autopilot PR superseded by another PR doesn't merge" "not the active autopilot attempt"

new_scenario autopilot-other-run
autopilot merge 7 556
expect_refusal "an autopilot PR from an older attempt doesn't merge" "not the active autopilot attempt"

new_scenario autopilot-label-removed
autopilot merge
echo '[]' >"$SCENARIO/issues_12_labels.json"
expect_refusal "an autopilot PR merges only while its issue carries the label" "no longer carries the autopilot label"

new_scenario autopilot-issue-closed
autopilot merge
echo '{"state":"closed"}' >"$SCENARIO/issues_12.json"
expect_refusal "an autopilot PR for a closed issue doesn't merge" "is closed"

new_scenario autopilot-forged-marker
autopilot todo
jq --arg b "<!-- autopilot: issue=12 run=555 stage=merge pr=7 -->" '. + [{body: $b, user: {login: "vanBassum", type: "User"}}]' \
  "$SCENARIO/issues_12_comments.json" >"$SCENARIO/c.tmp" && mv "$SCENARIO/c.tmp" "$SCENARIO/issues_12_comments.json"
expect_refusal "a state marker posted by a person doesn't count" "not the active autopilot attempt"

new_scenario autopilot-malformed-branch
autopilot merge
pr_field '.head.ref = "autopilot/whatever"'
expect_refusal "a malformed autopilot branch refuses" "doesn't name an issue and attempt"

new_scenario autopilot-api-error
autopilot merge
touch "$SCENARIO/issues_12_comments.fail"
expect_refusal "an API error reading the autopilot state refuses" "Could not read the comments"

new_scenario no-head-ref
pr_field 'del(.head.ref)'
expect_refusal "an unreadable head branch refuses" "Could not read the head branch"

new_scenario verdict-ok
if run_gate verdict api && grep -q "api: blocking=0" "$SCENARIO/out"; then
  PASS=$((PASS + 1)); echo "ok    verdict reports a reviewer's count"
else FAILED=$((FAILED + 1)); echo "FAIL  verdict reports a reviewer's count"; cat "$SCENARIO/out"; fi

new_scenario verdict-missing
drop_comments api
if ! run_gate verdict api && grep -q "No verdict from api" "$SCENARIO/out"; then
  PASS=$((PASS + 1)); echo "ok    verdict fails a reviewer that posted none"
else FAILED=$((FAILED + 1)); echo "FAIL  verdict fails a reviewer that posted none"; cat "$SCENARIO/out"; fi

echo "$PASS passed, $FAILED failed"
[ "$FAILED" -eq 0 ]
