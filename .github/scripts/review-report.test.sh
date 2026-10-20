#!/usr/bin/env bash
# Tests for review-report.sh against a fake `gh` that serves a PR head, check runs
# and comments, and records what it writes. Needs bash and jq.
# Run: bash .github/scripts/review-report.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
SCRIPT="$HERE/review-report.sh"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

SHA=1111111111111111111111111111111111111111
NEW=2222222222222222222222222222222222222222
MARKER='<!-- claude-review-report -->'

# --- fake gh, reading the case directory $CASE --------------------------------------
# heads: the PR's head SHA per read, one per line (the last one repeats).
# checks.json, comments.json: what the API serves. write: method, endpoint, body.
mkdir -p "$WORK/bin"
cat >"$WORK/bin/gh" <<'EOF'
#!/usr/bin/env bash
[ "$1" = api ] || { echo "fake gh: unsupported: $*" >&2; exit 2; }
shift
method=GET endpoint= filter=.
while [ $# -gt 0 ]; do
  case $1 in
    --method) method=$2; shift 2 ;;
    --jq) filter=$2; shift 2 ;;
    --paginate | --input) shift; [ "${1:-}" = - ] && shift ;;
    *) endpoint=$1; shift ;;
  esac
done
if [ "$method" != GET ]; then
  [ -e "$CASE/writefail" ] && { echo "HTTP 403: Forbidden" >&2; exit 1; }
  { echo "$method $endpoint"; jq -r .body; } >"$CASE/write"
  exit 0
fi
case $endpoint in
  */pulls/*)
    reads=$(($(cat "$CASE/reads" 2>/dev/null || echo 0) + 1)); echo "$reads" >"$CASE/reads"
    sed -n "$reads{p;q}; \$p" "$CASE/heads" | head -n 1 ;;
  */check-runs) jq -r "$filter" "$CASE/checks.json" ;;
  */comments) jq -r "$filter" "$CASE/comments.json" ;;
  *) echo "fake gh: unknown endpoint $endpoint" >&2; exit 2 ;;
esac
EOF
chmod +x "$WORK/bin/gh"
export PATH="$WORK/bin:$PATH"

# new_case <name> <head>...: a PR whose head reads as the given SHAs in turn.
new_case() {
  CASE="$WORK/$1"; export CASE; mkdir -p "$CASE"
  printf '%s\n' "${@:2}" >"$CASE/heads"
  echo '{"check_runs":[]}' >"$CASE/checks.json"
  echo '[]' >"$CASE/comments.json"
}
# verdict <id> <reviewer> <PASS|FIX|STOP|NONE> [summary]: a verdict check run.
verdict() {
  jq --argjson id "$1" --arg name "verdict · $2" --arg ext "$3" --arg summary "${4:-}" \
    '.check_runs += [{id: $id, name: $name, external_id: $ext, details_url: "https://run/\($id)", output: {summary: $summary}}]' \
    "$CASE/checks.json" >"$CASE/tmp" && mv "$CASE/tmp" "$CASE/checks.json"
}
# comment <id> <body> [login]: an existing PR comment.
comment() {
  jq --argjson id "$1" --arg body "$2" --arg login "${3:-github-actions[bot]}" \
    '. += [{id: $id, body: $body, user: {login: $login}}]' "$CASE/comments.json" >"$CASE/tmp" \
    && mv "$CASE/tmp" "$CASE/comments.json"
}

PASS=0; FAILED=0
ok() { PASS=$((PASS + 1)); echo "ok    $1"; }
no() { FAILED=$((FAILED + 1)); echo "FAIL  $1"; sed 's/^/      /' "$WORK/out" "$CASE/write" 2>/dev/null; }
run() { GITHUB_REPOSITORY=vanBassum/musichub PR=7 SHA=$SHA GH_TOKEN=x bash "$SCRIPT" >"$WORK/out" 2>&1; }
# wrote <description> <text>...: the run succeeded and its write holds every text.
wrote() {
  local text
  run || { no "$1 (the run failed)"; return; }
  for text in "${@:2}"; do grep -qF -- "$text" "$CASE/write" 2>/dev/null || { no "$1 (missing '$text')"; return; }; done
  ok "$1"
}

new_case post "$SHA"
verdict 1 api PASS "No blocking findings."
verdict 2 ui FIX "- [bug] A.tsx:3 — crash. Fix: guard."
verdict 3 understanding STOP "- [risk] B.cs:1 — UNDOCUMENTED. Decision: allow?"
verdict 4 smells NONE "No verdict from smells. Rerun the job."
wrote "the first report is a new comment with every verdict" \
  "POST repos/vanBassum/musichub/issues/7/comments" "Verdicts for \`${SHA:0:12}\`" \
  "### PASS — api" "### FIX — ui" "- [bug] A.tsx:3 — crash. Fix: guard." "[Review run](https://run/2)" \
  "### STOP — understanding" "### No verdict — smells" "$MARKER"
grep -qF "No blocking findings." "$CASE/write" && no "a PASS shows no summary" || ok "a PASS shows no summary"

new_case newest "$SHA"
verdict 5 api FIX "- [bug] old"
verdict 9 api PASS "No blocking findings."
wrote "only the newest verdict per reviewer counts" "### PASS — api"
grep -qF "### FIX — api" "$CASE/write" && no "an older verdict is shown" || ok "an older verdict is not shown"

new_case update "$SHA"
verdict 1 api PASS
comment 40 "### FIX — api (old per-reviewer comment)"
comment 41 "forged $MARKER" vanBassum
comment 42 "## Claude review
old
$MARKER"
wrote "an existing report is edited, not posted again" "PATCH repos/vanBassum/musichub/issues/comments/42" "### PASS — api"

new_case stale "$NEW"
verdict 1 api PASS
run && [ ! -e "$CASE/write" ] && grep -qF "no longer the head" "$WORK/out" \
  && ok "a run for an older commit writes nothing" || no "a run for an older commit writes nothing"

new_case overtaken "$SHA" "$NEW"
verdict 1 api PASS
run && [ ! -e "$CASE/write" ] && ok "a newer push during the run means no write" \
  || no "a newer push during the run means no write"

new_case long "$SHA"
verdict 1 api FIX "$(printf -- '- [bug] x%.0s\n' $(seq 1 3000))"
wrote "a long list is cut short" "cut short: the full list is in the check"
(($(wc -c <"$CASE/write") < 20000)) && ok "the cut list stays small" || no "the cut list stays small"

new_case empty "$SHA"
wrote "no verdict checks yet says so" "No verdicts yet."

new_case write-fails "$SHA"
touch "$CASE/writefail"
run && no "a failed write fails the job" || { grep -qF "Could not post" "$WORK/out" && ok "a failed write fails the job" || no "a failed write fails the job"; }

echo "$PASS passed, $FAILED failed"
[ "$FAILED" -eq 0 ]
