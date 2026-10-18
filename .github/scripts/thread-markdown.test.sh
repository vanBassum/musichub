#!/usr/bin/env bash
# Tests for thread-markdown.sh against a fake `gh` that serves canned JSON and
# records how it was called. Needs bash and jq.
# Run: bash .github/scripts/thread-markdown.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
TM="$HERE/thread-markdown.sh"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# --- fake gh --------------------------------------------------------------------
# `gh issue|pr view <n> --repo <r> --json <fields>` appends its arguments to
# $WORK/calls and prints $WORK/<kind>.json. Any other call fails.
mkdir -p "$WORK/bin"
cat >"$WORK/bin/gh" <<'EOF'
#!/usr/bin/env bash
echo "$*" >>"$WORK/calls"
[ "$2" = view ] && [ "$4" = --repo ] && [ "$6" = --json ] && [ $# = 7 ] || { echo "unexpected: $*" >&2; exit 1; }
cat "$WORK/$1.json"
EOF
chmod +x "$WORK/bin/gh"
export PATH="$WORK/bin:$PATH" WORK

PASS=0; FAILED=0
ok() { PASS=$((PASS + 1)); echo "ok    $1"; }
no() { FAILED=$((FAILED + 1)); echo "FAIL  $1"; }
has() { if grep -qF -- "$2" "$WORK/out"; then ok "$1"; else no "$1 (no '$2')"; sed 's/^/      /' "$WORK/out"; fi; }
lacks() { if grep -qF -- "$2" "$WORK/out"; then no "$1 (found '$2')"; else ok "$1"; fi; }
run() { : >"$WORK/calls"; bash "$TM" "$@" >"$WORK/out" 2>"$WORK/err"; }

cat >"$WORK/issue.json" <<'EOF'
{"number": 44, "title": "Catalog model", "author": {"login": "vanBassum"},
 "labels": [{"name": "autopilot"}, {"name": "autopilot:blocked"}],
 "body": "## Goal\nThe catalog.",
 "comments": [{"author": {"login": "github-actions"}, "createdAt": "2026-10-18T12:20:56Z", "body": "Autopilot is waiting."}]}
EOF
run issue vanBassum/musichub 44 && ok "an issue prints" || no "an issue prints ($(cat "$WORK/err"))"
has "with its number and title first" "# Issue #44: Catalog model"
has "its author" "Author: vanBassum"
has "its labels" "Labels: autopilot, autopilot:blocked"
has "its body, which --comments leaves out" "## Goal"
has "and its comments, with author and time" "### github-actions, 2026-10-18T12:20:56Z"
has "and their text" "Autopilot is waiting."
lacks "an issue has no branch" "Branch:"
if grep -qx "issue view 44 --repo vanBassum/musichub --json number,title,author,labels,body,comments" "$WORK/calls"; then
  ok "reads the issue as JSON, never with --comments"; else no "reads the issue as JSON, never with --comments ($(cat "$WORK/calls"))"; fi

cat >"$WORK/pr.json" <<'EOF'
{"number": 43, "title": "Register files", "author": {"login": "vanBassum"}, "labels": [],
 "headRefName": "autopilot/issue-27-r1", "body": "", "comments": []}
EOF
run pr vanBassum/musichub 43 && ok "a PR prints" || no "a PR prints ($(cat "$WORK/err"))"
has "with PR in its heading" "# PR #43: Register files"
has "its branch" "Branch: autopilot/issue-27-r1"
has "no labels says none" "Labels: none"
has "an empty body says so" "(no description)"
has "no comments says so" "(none)"
if grep -q -- "--json .*headRefName" "$WORK/calls" && ! grep -q statusCheckRollup "$WORK/calls"; then
  ok "reads the PR without its checks"; else no "reads the PR without its checks ($(cat "$WORK/calls"))"; fi

run issue vanBassum/musichub 0 && no "refuses a number that isn't one" || ok "refuses a number that isn't one"
run discussion vanBassum/musichub 4 && no "refuses another kind" || ok "refuses another kind"
: >"$WORK/calls"; rm "$WORK/issue.json"
run issue vanBassum/musichub 44 && no "fails when gh fails" || ok "fails when gh fails"

echo
echo "$PASS passed, $FAILED failed"
[ "$FAILED" = 0 ]
