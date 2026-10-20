#!/usr/bin/env bash
# Tests for review-verdict.sh against a fake `gh` that serves canned PR comments.
# Needs bash and jq. Run: bash .github/scripts/review-verdict.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
SCRIPT="$HERE/review-verdict.sh"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

SHA=1111111111111111111111111111111111111111
OLD=2222222222222222222222222222222222222222

# --- fake gh: `gh api <endpoint> --paginate --jq <filter>` over $COMMENTS -------
# A POST to check-runs writes its -f fields, one per line, to $COMMENTS.check.
mkdir -p "$WORK/bin"
cat >"$WORK/bin/gh" <<'EOF'
#!/usr/bin/env bash
[ "$1" = api ] || { echo "fake gh: unsupported: $*" >&2; exit 2; }
[ -e "$COMMENTS.fail" ] && { echo "HTTP 502: Bad Gateway" >&2; exit 1; }
if [[ $2 == */check-runs ]]; then
  [ -e "$COMMENTS.checkfail" ] && { echo "HTTP 403: Forbidden" >&2; exit 1; }
  : >"$COMMENTS.check"
  while [ $# -gt 0 ]; do case $1 in -f) printf '%s\n' "$2" >>"$COMMENTS.check"; shift 2 ;; *) shift ;; esac; done
  exit 0
fi
filter=.
while [ $# -gt 0 ]; do case $1 in --jq) filter=$2; shift 2 ;; *) shift ;; esac; done
jq -r "$filter" "$COMMENTS" | tr -d '\r'
EOF
chmod +x "$WORK/bin/gh"
export PATH="$WORK/bin:$PATH"

# new_case <name>: no comments yet.
new_case() { COMMENTS="$WORK/$1.json"; export COMMENTS; echo '[]' >"$COMMENTS"; }
# comment <body> [login] [type]: add a PR comment, by default from the bot.
comment() {
  jq --arg b "$1" --arg l "${2:-github-actions[bot]}" --arg t "${3:-Bot}" \
    '. + [{body: $b, user: {login: $l, type: $t}}]' "$COMMENTS" >"$COMMENTS.tmp" && mv "$COMMENTS.tmp" "$COMMENTS"
}
marker() { echo "<!-- claude-review: $1 sha=${3:-$SHA} blocking=$2 -->"; }

PASS=0; FAILED=0
# expect <description> <pass|fail> <text in the output> [reviewer]
expect() {
  local code=0
  GITHUB_REPOSITORY=vanBassum/musichub PR=7 SHA=$SHA GH_TOKEN=x bash "$SCRIPT" "${4:-api}" >"$WORK/out" 2>&1 || code=$?
  if { [ "$2" = pass ] && [ "$code" = 0 ]; } || { [ "$2" = fail ] && [ "$code" != 0 ]; }; then
    if grep -qF -- "$3" "$WORK/out"; then PASS=$((PASS + 1)); echo "ok    $1"; return; fi
  fi
  FAILED=$((FAILED + 1)); echo "FAIL  $1 (exit $code)"; sed 's/^/      /' "$WORK/out"
}

new_case pass
comment "### PASS — api
$(marker api 0)"
expect "blocking=0 for the head commit passes" pass "api: no blocking findings"

new_case blocking
comment "$(marker api 2)"
expect "a blocking finding fails" fail "api: 2 blocking finding(s)"

new_case missing
comment "$(marker ui 0)"
expect "no verdict from this reviewer fails" fail "No verdict from api"

new_case stale
comment "$(marker api 0 "$OLD")"
expect "a verdict for an older commit doesn't count" fail "No verdict from api"

new_case short-sha
comment "<!-- claude-review: api sha=${SHA:0:7} blocking=0 -->"
expect "a verdict with a short SHA doesn't count" fail "No verdict from api"

new_case malformed
comment "<!-- claude-review: api sha=$SHA blocking=none -->"
expect "a malformed verdict fails" fail "Malformed verdict from api"

new_case newer-pass
comment "$(marker api 1)"
comment "$(marker api 0)"
expect "a newer pass replaces an older block on the same commit" pass "api: no blocking findings"

new_case newer-block
comment "$(marker api 0)"
comment "$(marker api 1)"
expect "a newer block replaces an older pass" fail "api: 1 blocking finding(s)"

new_case forged
comment "$(marker api 0)" vanBassum User
expect "a verdict posted by a person doesn't count" fail "No verdict from api"

new_case similar-name
comment "$(marker api-extra 0)"
expect "another reviewer's similar name doesn't count" fail "No verdict from api"

new_case api-error
touch "$COMMENTS.fail"
expect "an API error fails" fail "Could not read"

# --- the verdict check ------------------------------------------------------------
# check <description> <line>...: the published check has each line as a field
# (name=value) or, for a findings line, as a line of the summary.
check() {
  local line missing=
  for line in "${@:2}"; do grep -qxF -- "$line" "$COMMENTS.check" 2>/dev/null || missing+=" '$line'"; done
  if [ -z "$missing" ]; then PASS=$((PASS + 1)); echo "ok    $1"
  else FAILED=$((FAILED + 1)); echo "FAIL  $1 (missing$missing)"; sed 's/^/      /' "$COMMENTS.check" 2>/dev/null; fi
}

new_case check-pass
comment "### PASS — api
$(marker api 0)"
expect "PASS passes" pass "api: no blocking findings"
check "PASS publishes a successful check on the head commit" \
  "name=verdict · api" "head_sha=$SHA" "conclusion=success" "external_id=PASS"

new_case check-fix
comment "### FIX — api
- [bug] musichub-api/A.cs:12 — null when empty. Fix: return [].
<!-- claude-review: api sha=$SHA blocking=1 -->"
expect "FIX fails" fail "api: 1 blocking finding(s)"
check "FIX publishes a failed check with the findings" \
  "conclusion=failure" "external_id=FIX" "output[title]=FIX — 1 blocking finding(s)" \
  "output[summary]=- [bug] musichub-api/A.cs:12 — null when empty. Fix: return []."

new_case check-stop
comment "### STOP — understanding
- [risk] musichub-api/B.cs:3 — Features: UNDOCUMENTED. Decision: may users export?
<!-- understanding sha=abc -->
$(marker understanding 1)"
expect "STOP fails" fail "understanding: 1 blocking finding(s)" understanding
check "STOP asks for a person, without hidden lines" \
  "name=verdict · understanding" "conclusion=action_required" "external_id=STOP" \
  "output[summary]=- [risk] musichub-api/B.cs:3 — Features: UNDOCUMENTED. Decision: may users export?"
grep -qF "understanding sha" "$COMMENTS.check" && { FAILED=$((FAILED + 1)); echo "FAIL  the hidden line is published"; }

new_case check-no-title
comment "Something went wrong.
$(marker api 2)"
expect "blocking without a title fails" fail "api: 2 blocking finding(s)"
check "blocking without a FIX or STOP title counts as STOP" "external_id=STOP"

new_case check-pass-title-blocking
comment "### PASS — api
$(marker api 1)"
expect "a PASS title doesn't pass blocking=1" fail "api: 1 blocking finding(s)"
check "a PASS title with blocking findings counts as STOP" "external_id=STOP"

new_case check-other-title
comment "### FIX — ui
- [bug] x
$(marker ui 1)"
comment "$(marker api 1)"
expect "another reviewer's title" fail "api: 1 blocking finding(s)"
check "another reviewer's title doesn't count" "external_id=STOP"

new_case check-none
expect "no verdict" fail "No verdict from api"
check "no verdict publishes NONE" "conclusion=failure" "external_id=NONE"

new_case check-publish-fails
comment "$(marker api 0)"
touch "$COMMENTS.checkfail"
expect "a check that can't be published fails the job" fail "Could not publish the verdict check"

echo "$PASS passed, $FAILED failed"
[ "$FAILED" -eq 0 ]
