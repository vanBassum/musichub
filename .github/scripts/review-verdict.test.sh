#!/usr/bin/env bash
# Tests for review-verdict.sh against a fake `gh` that records the published check.
# Needs bash and jq. Run: bash .github/scripts/review-verdict.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
SCRIPT="$HERE/review-verdict.sh"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

SHA=1111111111111111111111111111111111111111

# --- fake gh: a POST to check-runs writes its -f fields, one per line, to $CHECK --
mkdir -p "$WORK/bin"
cat >"$WORK/bin/gh" <<'EOF'
#!/usr/bin/env bash
[ "$1" = api ] && [[ $2 == */check-runs ]] || { echo "fake gh: unsupported: $*" >&2; exit 2; }
[ -e "$CHECK.fail" ] && { echo "HTTP 403: Forbidden" >&2; exit 1; }
: >"$CHECK"
while [ $# -gt 0 ]; do case $1 in -f) printf '%s\n' "$2" >>"$CHECK"; shift 2 ;; *) shift ;; esac; done
exit 0
EOF
chmod +x "$WORK/bin/gh"
export PATH="$WORK/bin:$PATH"

# new_case <name> [review json]: the reviewer's structured output for this case.
new_case() { CHECK="$WORK/$1.check"; export CHECK; REVIEW_JSON=${2:-}; UNDERSTANDING_SHA=; }

PASS=0; FAILED=0
# expect <description> <pass|fail> <text in the output> [reviewer]
expect() {
  local code=0
  GITHUB_REPOSITORY=vanBassum/musichub SHA=$SHA GH_TOKEN=x REVIEW_JSON=$REVIEW_JSON \
    UNDERSTANDING_SHA=$UNDERSTANDING_SHA bash "$SCRIPT" "${4:-api}" >"$WORK/out" 2>&1 || code=$?
  if { [ "$2" = pass ] && [ "$code" = 0 ]; } || { [ "$2" = fail ] && [ "$code" != 0 ]; }; then
    if grep -qF -- "$3" "$WORK/out"; then PASS=$((PASS + 1)); echo "ok    $1"; return; fi
  fi
  FAILED=$((FAILED + 1)); echo "FAIL  $1 (exit $code)"; sed 's/^/      /' "$WORK/out"
}
# check <description> <line>...: the published check has each line as a field
# (name=value) or, for a findings line, as a line of the summary.
check() {
  local line missing=
  for line in "${@:2}"; do grep -qxF -- "$line" "$CHECK" 2>/dev/null || missing+=" '$line'"; done
  if [ -z "$missing" ]; then PASS=$((PASS + 1)); echo "ok    $1"
  else FAILED=$((FAILED + 1)); echo "FAIL  $1 (missing$missing)"; sed 's/^/      /' "$CHECK" 2>/dev/null; fi
}

new_case pass '{"verdict":"PASS","blocking":0,"findings":[]}'
expect "PASS passes" pass "api: no blocking findings"
check "PASS publishes a successful check on the head commit" \
  "name=verdict · api" "head_sha=$SHA" "conclusion=success" "external_id=PASS" "output[summary]=No blocking findings."

new_case fix '{"verdict":"FIX","blocking":1,"findings":["[bug] musichub-api/A.cs:12 — null when empty. Fix: return []."]}'
expect "FIX fails" fail "api: 1 blocking finding(s)"
check "FIX publishes a failed check with the findings" \
  "conclusion=failure" "external_id=FIX" "output[title]=FIX — 1 blocking finding(s)" \
  "output[summary]=- [bug] musichub-api/A.cs:12 — null when empty. Fix: return []."

new_case stop '{"verdict":"STOP","blocking":2,"findings":["[risk] musichub-api/B.cs:3 — Features: UNDOCUMENTED. Decision: may users export?","- [risk] C.cs:1 — two\nlines. Fix: x."]}'
UNDERSTANDING_SHA=abc123
expect "STOP fails" fail "understanding: 2 blocking finding(s)" understanding
check "STOP asks for a person, one line per finding" \
  "name=verdict · understanding" "conclusion=action_required" "external_id=STOP" \
  "output[summary]=- [risk] musichub-api/B.cs:3 — Features: UNDOCUMENTED. Decision: may users export?" \
  "- [risk] C.cs:1 — two lines. Fix: x." \
  "output[text]=Checked against musichub-understanding abc123."

new_case pass-word-blocking '{"verdict":"PASS","blocking":1,"findings":["[bug] x"]}'
expect "a PASS verdict doesn't pass blocking=1" fail "api: 1 blocking finding(s)"
check "a PASS verdict with blocking findings counts as STOP" "external_id=STOP"

new_case blocking-zero-fix '{"verdict":"FIX","blocking":0,"findings":[]}'
expect "blocking=0 passes whatever the verdict word" pass "api: no blocking findings"
check "blocking=0 publishes PASS" "external_id=PASS"

new_case none
expect "no structured output fails" fail "No verdict from api"
check "no verdict publishes NONE" "conclusion=failure" "external_id=NONE"

for bad in 'not json' '[]' '{"verdict":"MAYBE","blocking":0,"findings":[]}' \
  '{"verdict":"FIX","blocking":"1","findings":[]}' '{"verdict":"FIX","blocking":-1,"findings":[]}' \
  '{"verdict":"FIX","blocking":1.5,"findings":[]}' '{"verdict":"FIX","blocking":1}' \
  '{"verdict":"FIX","blocking":1,"findings":[3]}'; do
  new_case malformed "$bad"
  expect "malformed: $bad" fail "Malformed verdict from api"
  check "malformed publishes NONE: $bad" "external_id=NONE"
done

new_case publish-fails '{"verdict":"PASS","blocking":0,"findings":[]}'
touch "$CHECK.fail"
expect "a check that can't be published fails the job" fail "Could not publish the verdict check"

echo "$PASS passed, $FAILED failed"
[ "$FAILED" -eq 0 ]
