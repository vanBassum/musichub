#!/usr/bin/env bash
# Tests for autofix.sh against a fake `gh` that serves a canned PR, its checks and
# the Autofix runs, and records what the script starts. Needs bash and jq.
# Run: bash .github/scripts/autofix.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
SCRIPT="$HERE/autofix.sh"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

SHA=1111111111111111111111111111111111111111

# --- fake gh over the files in $CASE ---------------------------------------------
# pr.json (a list: one PR per `gh pr view`, the last one repeats), checks.json,
# runs.json, attempt; every workflow run and rerun is appended to $CASE/started.
mkdir -p "$WORK/bin"
cat >"$WORK/bin/gh" <<'EOF'
#!/usr/bin/env bash
case "$1 $2" in
  "pr view")
    n=$(cat "$CASE/pr-views" 2>/dev/null || echo 0); echo $((n + 1)) >"$CASE/pr-views"
    jq -c "if length > $n then .[$n] else .[-1] end" "$CASE/pr.json" ;;
  "run list") cat "$CASE/runs.json" ;;
  "workflow run" | "run rerun") echo "$*" >>"$CASE/started" ;;
  api*)
    filter=.
    while [ $# -gt 0 ]; do case $1 in --jq) filter=$2; shift 2 ;; *) shift ;; esac; done
    if [[ $filter == .run_attempt ]]; then cat "$CASE/attempt"; else jq -c "$filter" "$CASE/checks.json"; fi ;;
  *) echo "fake gh: unsupported: $*" >&2; exit 2 ;;
esac
EOF
chmod +x "$WORK/bin/gh"
export PATH="$WORK/bin:$PATH" MERGEABLE_POLL_SECONDS=0

# pr [field=json]...: a PR of Claude's that is open, mergeable and touches the API;
# each argument overrides one field. Call it again to queue the next `gh pr view`.
pr() {
  local json
  json=$(jq -n --arg sha "$SHA" '{state: "OPEN", isDraft: false, headRefName: "claude/issue-1",
    headRefOid: $sha, mergeable: "MERGEABLE", files: [{path: "musichub-api/A.cs"}]}')
  for kv in "$@"; do json=$(jq -c --argjson v "${kv#*=}" ".${kv%%=*} = \$v" <<<"$json"); done
  jq -c --argjson p "$json" '. + [$p]' "$CASE/pr.json" >"$CASE/pr.tmp" && mv "$CASE/pr.tmp" "$CASE/pr.json"
}
# check <name> <status> [conclusion] [external_id] [id]: a check run on the head commit.
check() {
  jq -c --arg n "$1" --arg s "$2" --arg c "${3:-}" --arg e "${4:-}" --argjson id "${5:-1}" \
    '.check_runs += [{id: $id, name: $n, status: $s, conclusion: (if $c == "" then null else $c end),
      external_id: $e, details_url: "https://github.com/vanBassum/musichub/actions/runs/77"}]' \
    "$CASE/checks.json" >"$CASE/checks.tmp" && mv "$CASE/checks.tmp" "$CASE/checks.json"
}
# green: a passed build and four passing reviewers.
green() {
  check build completed success
  for r in api ui understanding smells; do check "verdict · $r" completed success PASS; done
}
# autofix_runs <status>...: earlier Autofix runs of this PR.
autofix_runs() {
  local s
  for s in "$@"; do
    jq -c --arg s "$s" '. + [{displayTitle: "Autofix #7", status: $s}]' "$CASE/runs.json" >"$CASE/runs.tmp" \
      && mv "$CASE/runs.tmp" "$CASE/runs.json"
  done
}
new_case() {
  CASE="$WORK/$1"; export CASE; mkdir -p "$CASE"
  echo '[]' >"$CASE/pr.json"; echo '{"check_runs": []}' >"$CASE/checks.json"
  echo '[{"displayTitle": "Autofix #77", "status": "in_progress"}]' >"$CASE/runs.json"
  echo 1 >"$CASE/attempt"
}

PASS=0; FAILED=0
# expect <description> <output line> [started command]: the script prints the line
# and starts exactly that command, or nothing.
expect() {
  local out started
  out=$(GITHUB_REPOSITORY=vanBassum/musichub GH_TOKEN=x bash "$SCRIPT" 7 2>&1)
  started=$(cat "$CASE/started" 2>/dev/null)
  if grep -qF -- "$2" <<<"$out" && [ "$started" = "${3:-}" ]; then PASS=$((PASS + 1)); echo "ok    $1"
  else FAILED=$((FAILED + 1)); echo "FAIL  $1"; echo "      output: $out"; echo "      started: ${started:-nothing}"; fi
}
FIX_RUN="workflow run claude.yml -R vanBassum/musichub --ref main -f pr=7 -f sha=$SHA"

# --- which PRs ---------------------------------------------------------------------
new_case closed; pr state='"MERGED"'; green
expect "a closed PR is skipped" "skip: the PR is not open"

new_case draft; pr isDraft=true; green
expect "a draft is skipped" "skip: the PR is a draft"

new_case human; pr headRefName='"fix/x"'; check build completed failure
expect "a PR that isn't Claude's is skipped" "skip: not a claude/ branch"

new_case workflow-files; pr files='[{"path":"musichub-api/A.cs"},{"path":".github/workflows/build.yml"}]'
check build completed failure
expect "a PR touching .github/ is skipped" "skip: the PR touches .github/"

# --- waiting ---------------------------------------------------------------------
new_case running; pr; autofix_runs completed in_progress; check build completed failure
expect "an Autofix run in progress waits" "wait: an Autofix run for this PR is queued or running"

new_case no-build; pr
expect "no build yet waits" "wait: the build hasn't finished"

new_case build-running; pr; check build in_progress
expect "a running build waits" "wait: the build hasn't finished"

new_case build-cancelled; pr; check build completed cancelled
expect "a cancelled build waits" "wait: the build didn't finish (cancelled)"

new_case no-verdict-yet; pr; check build completed success; check "verdict · api" completed success PASS
expect "a missing verdict waits" "wait: no verdict from ui yet"

# --- conflicts -----------------------------------------------------------------------
new_case conflict; pr mergeable='"CONFLICTING"'
expect "a conflict starts an Autofix run" "fix: the PR conflicts with main (Autofix run 1 of 5)" "$FIX_RUN"

new_case conflict-late; pr mergeable='"UNKNOWN"'; pr mergeable='"UNKNOWN"'; pr mergeable='"CONFLICTING"'
expect "mergeability is polled until GitHub knows it" "fix: the PR conflicts with main" "$FIX_RUN"

new_case conflict-limit; pr mergeable='"CONFLICTING"'; autofix_runs completed completed completed completed completed
expect "after five Autofix runs it stops" "stop: the PR conflicts with main, but this PR already had 5 Autofix runs"

# --- verdicts --------------------------------------------------------------------
new_case green; pr; green
expect "all green is done" "done: the build and all reviewers passed $SHA"

new_case fix; pr; check build completed success
check "verdict · api" completed success PASS; check "verdict · ui" completed failure FIX
check "verdict · understanding" completed success PASS; check "verdict · smells" completed failure FIX
autofix_runs completed completed
expect "FIX starts an Autofix run" "fix: FIX from ui smells (Autofix run 3 of 5)" "$FIX_RUN"

new_case build-fails; pr; green; check build completed skipped "" 2
expect "a failed build starts an Autofix run" "fix: the build fails (Autofix run 1 of 5)" "$FIX_RUN"

new_case stop; pr; check build completed failure
check "verdict · api" completed success PASS; check "verdict · ui" completed success PASS
check "verdict · understanding" completed action_required STOP; check "verdict · smells" completed failure FIX
expect "STOP stops, also with a FIX" "stop: STOP from understanding: a decision for Bas"

new_case newest; pr; green; check "verdict · smells" completed failure FIX 0
expect "the newest check per name counts" "done:"

new_case none; pr; green; check "verdict · api" completed failure NONE 2
expect "no verdict reruns the review run" "rerun: a reviewer left no verdict; rerunning review run 77" \
  "run rerun 77 --failed -R vanBassum/musichub"

new_case none-limit; pr; green; check "verdict · api" completed failure NONE 2; echo 3 >"$CASE/attempt"
expect "no verdict after three attempts stops" "stop: a reviewer left no verdict in 3 attempts"

new_case other-pr; pr; green; check build completed failure "" 2
expect "another PR's Autofix runs don't count" "fix: the build fails (Autofix run 1 of 5)" "$FIX_RUN"

echo "$PASS passed, $FAILED failed"
[ "$FAILED" -eq 0 ]
