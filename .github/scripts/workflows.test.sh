#!/usr/bin/env bash
# Checks the settings every anthropics/claude-code-action step in the workflows must
# have, and that the check itself catches a step without them. Needs bash and awk.
# Run: bash .github/scripts/workflows.test.sh
#
# classify_inline_comments: "false". Otherwise the action buffers inline comments in
# one fixed file, /tmp/inline-comments-buffer.jsonl, never empties it, and after
# every run posts what is in it to "the PR" of that run's event. Our runner is
# self-hosted and shared, so a reviewer posted another reviewer's comments, from
# another PR, as its own.
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
WORKFLOWS="$HERE/../workflows"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# claude_step_setting <key> <workflow>...: prints "<file>:<line> <value>" for every
# claude-code-action step: the value of <key> under the step's own `with:`, unquoted
# and without a trailing comment, or "-" when the step doesn't set it.
claude_step_setting() {
  awk -v key="$1" '
    function flush() {
      if (claude) print file ":" start " " (val == "" ? "-" : val)
      claude = 0; val = ""; start = 0; wi = -1
    }
    FNR == 1 { flush(); insteps = 0 }
    { sub(/\r$/, "") }
    /^[[:space:]]*(#.*)?$/ { next }
    { match($0, /^ */); ind = RLENGTH }
    insteps && ind <= sk { flush(); insteps = 0 }
    /^ *steps:[[:space:]]*$/ { flush(); insteps = 1; sk = ind; d = -1; next }
    insteps && d < 0 && /^ *- / { d = ind }
    insteps && ind == d && /^ *- / { flush(); start = FNR; file = FILENAME }
    start && /uses:[[:space:]]*anthropics\/claude-code-action@/ { claude = 1 }
    start && /^ *with:[[:space:]]*$/ { wi = ind }
    # A key of `with:`, not a line in a block such as the prompt.
    start && wi >= 0 && ind == wi + 2 && $0 ~ ("^ *" key ":") {
      val = $0; sub(/^ *[A-Za-z0-9_-]+:[[:space:]]*/, "", val); sub(/[[:space:]]*(#.*)?$/, "", val)
      gsub(/["\047]/, "", val)
    }
    END { flush() }
  ' "${@:2}"
}

# claude_steps <all|buffered> <workflow>...: prints "<file>:<line>" for every
# claude-code-action step (all), or only those that don't set
# classify_inline_comments to false within the step itself (buffered).
claude_steps() {
  claude_step_setting classify_inline_comments "${@:2}" \
    | awk -v want="$1" '{ v = $NF; sub(/ [^ ]*$/, "") } want == "all" || v != "false"'
}

PASS=0; FAILED=0
ok() { PASS=$((PASS + 1)); echo "ok    $1"; }
no() { FAILED=$((FAILED + 1)); echo "FAIL  $1"; }
# expect <description> <want count> <all|buffered> <workflow>...
expect() {
  local got
  got=$(claude_steps "$3" "${@:4}" | grep -c .)
  if [ "$got" = "$2" ]; then ok "$1"; else no "$1 (found $got, want $2)"; claude_steps "$3" "${@:4}" | sed 's/^/      /'; fi
}

# --- the real workflows -------------------------------------------------------
expect "the reviewers run Claude in one step" 1 all "$WORKFLOWS/claude-review.yml"
expect "claude.yml runs Claude in one step" 1 all "$WORKFLOWS/claude.yml"
expect "no claude-code-action step buffers inline comments" 0 buffered "$WORKFLOWS"/*.yml

# The reviewers only read and comment inline: they never change files, run code or
# post PR comments (their verdict is structured output; review-report.sh posts it).
reviewer_tools=$(sed -nE 's/^[[:space:]]*--allowedTools "([^"]*)"[[:space:]]*\r?$/\1/p' "$WORKFLOWS/claude-review.yml")
[ "$reviewer_tools" = 'Read,Grep,Glob,mcp__github_inline_comment__create_inline_comment,Bash(gh pr diff:*),Bash(gh pr view:*),Bash(git log:*),Bash(git show:*)' ] \
  && ok "the reviewers only read and comment" || no "the reviewers only read and comment (found: $reviewer_tools)"

# --- the check catches what it should ------------------------------------------
fixture() { printf '%s\n' "$2" >"$WORK/$1.yml"; echo "$WORK/$1.yml"; }

f=$(fixture default 'jobs:
  analysis:
    runs-on: x
    steps:
      - uses: actions/checkout@v4
      - name: Claude
        uses: anthropics/claude-code-action@v1
        with:
          prompt: |
            - a list item in the prompt
            classify_inline_comments: "false" is not a setting in here
          claude_args: --max-turns 1')
expect "a step with the default setting is caught" 1 buffered "$f"

f=$(fixture true 'jobs:
  a:
    steps:
      - name: Claude
        uses: anthropics/claude-code-action@v1
        with:
          classify_inline_comments: "true"')
expect "classify_inline_comments: true is caught" 1 buffered "$f"

f=$(fixture other-step 'jobs:
  a:
    steps:
      - name: Claude
        uses: anthropics/claude-code-action@v1
        with:
          prompt: hi
      - name: Other
        uses: some/action@v1
        with:
          classify_inline_comments: "false"
  b:
    steps:
      - uses: anthropics/claude-code-action@v1
  c:
    runs-on: x
    env:
      classify_inline_comments: "false"')
expect "the setting on another step or job doesn't count" 2 buffered "$f"

f=$(fixture off $'jobs:\r
  a:\r
    steps:\r
      - uses: anthropics/claude-code-action@v1\r
        with:\r
          classify_inline_comments: false  # comment\r
      - name: Claude\r
        id: claude\r
        uses: anthropics/claude-code-action@v1\r
        with:\r
          github_token: x\r
          classify_inline_comments: \'false\'\r
  b:\r
    steps:\r
      - name: Claude\r
        uses: anthropics/claude-code-action@v1\r
        with:\r
          classify_inline_comments: "false"')
expect "every spelling of false passes (also with CRLF)" 0 buffered "$f"
expect "and all three steps are seen" 3 all "$f"

echo "$PASS passed, $FAILED failed"
[ "$FAILED" -eq 0 ]
