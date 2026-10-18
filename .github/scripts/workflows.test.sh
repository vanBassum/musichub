#!/usr/bin/env bash
# Checks the settings every anthropics/claude-code-action step in the workflows must
# have, and that the check itself catches a step without them. Needs bash and awk.
# Run: bash .github/scripts/workflows.test.sh
#
# classify_inline_comments: "false". Otherwise the action buffers inline comments in
# one fixed file, /tmp/inline-comments-buffer.jsonl, never empties it, and after
# every run posts what is in it to "the PR" of that run's event. Our runner is
# self-hosted and shared, so a reviewer posted another reviewer's comments, from
# another PR, as its own; and an autopilot analysis of an issue, which has no PR,
# failed on GET /pulls/<issue> (404) after Claude had finished (DB4045 #289).
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

# claude_bots <workflow>...: prints "<file>:<line> <allowed_bots>" for every
# claude-code-action step, "-" when it allows no bot. The action refuses a run started
# by a bot unless the step names that bot; each step names only the one it needs.
claude_bots() { claude_step_setting allowed_bots "$@"; }

# claude_jobs_with_write [<permission>] <workflow>...: prints "<file>:<job>" for every
# job that runs claude-code-action or the sandbox (sandbox.sh run: Claude's build and
# fix, the PR's own code) and also has a write permission (given one, that write
# permission), mints a GitHub App token or references AUTOPILOT_PAT or
# AUTOPILOT_BOARD_TOKEN. Autopilot runs Claude and unmerged code only in read-only
# jobs; shell jobs do the writing.
claude_jobs_with_write() {
  local perm='[A-Za-z0-9_-]+'
  case $1 in *.yml) ;; *) perm=$1; shift ;; esac
  awk -v perm="$perm" '
    function flush() { if (job != "" && claude && (write || app)) print file ":" job; job = ""; claude = 0; write = 0; app = 0; inperm = 0 }
    FNR == 1 { flush(); injobs = 0 }
    { sub(/\r$/, "") }
    /^[[:space:]]*(#.*)?$/ { next }
    { match($0, /^ */); ind = RLENGTH }
    ind == 0 { flush(); injobs = ($0 ~ /^jobs:/); next }
    injobs && ind == 2 && /^  [A-Za-z0-9_-]+:[[:space:]]*$/ { flush(); job = $1; sub(/:$/, "", job); file = FILENAME; next }
    job != "" && ind == 4 { inperm = ($0 ~ /^    permissions:/) }
    job != "" && inperm && ind > 4 && $0 ~ ("^ *" perm ":[[:space:]]*write") { write = 1 }
    job != "" && /uses:[[:space:]]*anthropics\/claude-code-action@/ { claude = 1 }
    job != "" && /sandbox\.sh run / { claude = 1 }
    job != "" && /uses:[[:space:]]*actions\/create-github-app-token@/ { app = 1 }
    job != "" && /secrets\.AUTOPILOT_(PAT|BOARD_TOKEN)/ { app = 1 }
    END { flush() }
  ' "$@"
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
expect "autopilot runs claude-code-action in one step (the investigation; the build runs in the sandbox)" 1 all "$WORKFLOWS/autopilot.yml"
expect "the fix round runs no claude-code-action (it runs in the sandbox)" 0 all "$WORKFLOWS/autopilot-fix.yml"
expect "no claude-code-action step buffers inline comments" 0 buffered "$WORKFLOWS"/*.yml

# expect_read_only <description> <want count> <workflow>...
expect_read_only() {
  local got
  got=$(claude_jobs_with_write "${@:3}" | grep -c .)
  if [ "$got" = "$2" ]; then ok "$1"; else no "$1 (found $got, want $2)"; claude_jobs_with_write "${@:3}" | sed 's/^/      /'; fi
}
expect_read_only "Claude and the sandbox run only in jobs without write access, an App token or the PAT" 0 \
  "$WORKFLOWS/autopilot.yml" "$WORKFLOWS/autopilot-fix.yml" "$WORKFLOWS/build.yml"

# --- which bots may start Claude ---------------------------------------------------
# The action refuses a run started by a bot unless its step names the bot. A waiting
# investigation is started again by the queue (autopilot-queue.yml) with
# GITHUB_TOKEN, so it names github-actions; that is safe only while no job that runs
# Claude can dispatch a workflow (actions: write, an App token or the PAT), so Claude
# can't start Claude.
# expect_no_dispatch <description> <want count> <workflow>...
expect_no_dispatch() {
  local got
  got=$(claude_jobs_with_write actions "${@:3}" | grep -c .)
  if [ "$got" = "$2" ]; then ok "$1"; else no "$1 (found $got, want $2)"; claude_jobs_with_write actions "${@:3}" | sed 's/^/      /'; fi
}
expect_no_dispatch "no job that runs Claude holds actions: write, an App token or the PAT" 0 "$WORKFLOWS"/*.yml

# expect_bots <description> <want, one line per Claude step> <workflow>...
expect_bots() {
  local got
  got=$(claude_bots "${@:3}" | sed 's/^[^ ]* //')
  if [ "$got" = "$2" ]; then ok "$1"; else no "$1"; claude_bots "${@:3}" | sed 's/^/      /'; fi
}
expect_bots "the reviewers allow no bot (the owner's PAT opens autopilot PRs)" - "$WORKFLOWS/claude-review.yml"
expect_bots "autopilot's investigation allows only github-actions (the queue dispatches a waiting one)" github-actions "$WORKFLOWS/autopilot.yml"

# --- autopilot's tool permissions --------------------------------------------------
# In an unattended run --allowedTools is the whole permission policy: a tool that is
# not listed is refused ("requires approval"), since nobody can approve it. DB4045
# issue #301's build could create and edit files but not move or delete one, so a rename
# could never be done. Each list is pinned here: what the step needs must be in it,
# and nothing outside its approved set may be (no unscoped Bash, no mv/rm/sed, no
# network, no push, no GitHub writes).

# allowed_tools <workflow>: the --allowedTools list of each Claude step, one per line,
# entries separated by commas.
allowed_tools() { sed -nE 's/^[[:space:]]*--allowedTools "([^"]*)"[[:space:]]*\r?$/\1/p' "$1"; }
# unapproved <approved entries, one per line> <list>: the list's entries not approved.
unapproved() { tr ',' '\n' <<<"$2" | grep -vxF -f <(printf '%s\n' "$1") || true; }
# missing <required entries, one per line> <list>: required entries not in the list.
missing() { printf '%s\n' "$1" | grep -vxF -f <(tr ',' '\n' <<<"$2") || true; }
# expect_none <description> <output>
expect_none() { if [ -z "$2" ]; then ok "$1"; else no "$1: $(tr '\n' ' ' <<<"$2")"; fi; }

READ_TOOLS='Read
Grep
Glob
Bash(gh issue view:*)
Bash(gh issue list:*)
Bash(gh pr list:*)
Bash(gh pr view:*)
Bash(gh pr diff:*)
Bash(git log:*)
Bash(git show:*)
Bash(git status:*)
Bash(git diff:*)'
# The build and the fix round (sandbox-job.sh): the read tools, plus changing files
# in the checkout, committing, building and linting the API and the UI, and the two
# scripts that add a shadcn/ui component or a package. Not the test suite: the
# understanding has implementation agents address test failures from CI results
# (architecture, "Delivery pipeline"); the pipeline runs the tests.
WRITE_TOOLS="$READ_TOOLS
Write
Edit
Bash(git add:*)
Bash(git commit:*)
Bash(git mv:*)
Bash(git rm:*)
Bash(dotnet restore:*)
Bash(dotnet build:*)
Bash(dotnet ef:*)
Bash(pnpm --dir musichub-ui lint:*)
Bash(pnpm --dir musichub-ui typecheck:*)
Bash(pnpm --dir musichub-ui build:*)
Bash(pnpm --dir musichub-ui format:*)
Bash(bash /work/trusted/shadcn-components.sh:*)
Bash(bash /work/trusted/add-package.sh:*)"
# What a build needs to carry out any admitted change: create, change, move and
# delete files, then commit.
NEEDED='Read
Write
Edit
Bash(git mv:*)
Bash(git rm:*)
Bash(git add:*)
Bash(git commit:*)'

analysis=$(allowed_tools "$WORKFLOWS/autopilot.yml")
build=$(sed -nE 's/^TOOLS="([^"]*)"[[:space:]]*\r?$/\1/p' "$HERE/sandbox-job.sh")
[ "$(grep -c . <<<"$analysis")" = 1 ] && [ "$(grep -c . <<<"$build")" = 1 ] \
  && ok "the investigation and the sandbox's Claude runs each have one --allowedTools list" || no "the investigation and the sandbox's Claude runs each have one --allowedTools list"
[ -z "$(allowed_tools "$WORKFLOWS/autopilot-fix.yml")" ] && ok "the fix round's list is the sandbox's" || no "the fix round's list is the sandbox's"
grep -qF -- '--allowedTools "$TOOLS"' "$HERE/sandbox-job.sh" && ok "and sandbox-job.sh passes it to Claude" || no "and sandbox-job.sh passes it to Claude"
expect_none "the build and the fix round may create, change, move and delete files and commit them" "$(missing "$NEEDED" "$build")"
expect_none "they have no tool beyond their approved set" "$(unapproved "$WRITE_TOOLS" "$build")"
expect_none "the investigation only reads" "$(unapproved "$READ_TOOLS" "$analysis")"
expect_none "the build in issue #301's run lacked moves and deletes (the check sees it)" \
  "$([ -n "$(missing "$NEEDED" "Read,Write,Edit,Bash(git add:*),Bash(git commit:*)")" ] || echo "not caught")"
for bad in 'Bash' 'Bash(*)' 'Bash(rm:*)' 'Bash(mv:*)' 'Bash(sed:*)' 'Bash(cp:*)' 'Bash(curl:*)' 'Bash(git push:*)' \
           'Bash(git config:*)' 'Bash(gh pr create:*)' 'Bash(gh api:*)' 'Bash(pnpm:*)' 'Bash(pnpm add:*)' 'Bash(pnpm install:*)' 'Bash(npx:*)' \
           'Bash(bash:*)' 'Bash(dotnet:*)' 'Bash(dotnet add:*)' 'Bash(dotnet test:*)' 'WebFetch' 'WebSearch'; do
  expect_none "an approved set never lets '$bad' through" "$([ -z "$(unapproved "$WRITE_TOOLS" "Read,$bad")" ] && echo "let through")"
done

# --- the sandbox ------------------------------------------------------------------
# Code that no person has merged runs only in the sandbox (sandbox.sh): the PR's
# restore, build and tests, and Claude's build and fix. On the runner it could leave
# something behind for a later job that holds the PAT.

# run_lines <workflow>...: "<file>:<line> <text>" for every line of a `run:` step.
run_lines() {
  awk '
    { sub(/\r$/, "") }
    { match($0, /^ */); ind = RLENGTH }
    inrun && ind <= rind && $0 !~ /^[[:space:]]*$/ { inrun = 0 }
    inrun { print FILENAME ":" FNR " " $0; next }
    /^ *(- )?run:/ {
      line = $0; sub(/^ *(- )?run:[[:space:]]*/, "", line); rind = ind
      if (line ~ /^[|>]/) inrun = 1; else print FILENAME ":" FNR " " line
    }
  ' "$@"
}
product=$(run_lines "$WORKFLOWS"/*.yml | grep -E 'dotnet (restore|build|test|publish|run|ef)|pnpm |corepack|npx |npm (install|ci|run)' \
  | grep -vF 'npm install --global --silent @anthropic-ai/claude-code@' || true)
expect_none "no workflow restores, builds or tests product code on the runner" "$product"
probes=$(run_lines "$WORKFLOWS"/*.yml | grep -E '(^|[^a-z-])claude -p' || true)
expect_none "a claude run on the runner is only the usage probe, without tools" "$(grep -vF -- '--tools ""' <<<"$probes")"
[ "$(grep -c . <<<"$probes")" = 2 ] && ok "and there are two: autopilot's start and the fix round's" || no "the probes (found: $probes)"

# jobs_with <pattern> <workflow>...: "<file>:<job>" for every job with a line that
# matches <pattern>.
jobs_with() {
  awk -v pat="$1" '
    FNR == 1 { injobs = 0; job = "" }
    { sub(/\r$/, "") }
    /^[[:space:]]*(#.*)?$/ { next }
    { match($0, /^ */); ind = RLENGTH }
    ind == 0 { injobs = ($0 ~ /^jobs:/); job = ""; next }
    injobs && ind == 2 && /^  [A-Za-z0-9_-]+:[[:space:]]*$/ { job = $1; sub(/:$/, "", job); next }
    job != "" && $0 ~ pat { print FILENAME ":" job }
  ' "${@:2}" | sort -u
}
holders=$(jobs_with 'secrets[.]AUTOPILOT_(PAT|BOARD_TOKEN)' "$WORKFLOWS"/*.yml)
sandboxed=$(jobs_with 'sandbox[.]sh run ' "$WORKFLOWS"/*.yml)
expect_none "no job that holds the PAT or the board token runs the sandbox" "$(comm -12 <(echo "$holders") <(echo "$sandboxed"))"
pr_code=$(jobs_with 'ref:.*(inputs[.]sha|pull_request[.]head)' "$WORKFLOWS"/*.yml)
expect_none "no job that holds the PAT or the board token checks out a PR's code" "$(comm -12 <(echo "$holders") <(echo "$pr_code"))"
[ "$(grep -c . <<<"$sandboxed")" = 3 ] && ok "three jobs run the sandbox: the build, the fix round and the PR checks" || no "the sandbox jobs (found: $sandboxed)"
cleaned=$(jobs_with 'sandbox[.]sh clean ' "$WORKFLOWS"/*.yml)
expect_none "every job that runs the sandbox removes it again" "$(comm -23 <(echo "$sandboxed") <(echo "$cleaned"))"
expect_none "no SANDBOX_ parameter is a secret or a GitHub token" \
  "$(grep -hE '^[[:space:]]*SANDBOX_[A-Z0-9_]+:' "$WORKFLOWS"/*.yml | grep -E 'secrets\.|github\.token|GH_TOKEN|GITHUB_TOKEN' || true)"
expect_none "sandbox.sh names no other variable for the container than HOME" \
  "$(grep -oE -- '-e [A-Z_]+' "$HERE/sandbox.sh" | grep -vx -- '-e HOME' || true)"
grep -qF 'for v in CLAUDE_CODE_OAUTH_TOKEN ANTHROPIC_API_KEY $(compgen -e | grep -E' "$HERE/sandbox.sh" \
  && ok "the container's environment is Claude's credential and the SANDBOX_ variables" || no "the container's environment is Claude's credential and the SANDBOX_ variables"
grep -qE 'docker\.sock|--privileged|--network host' "$HERE/sandbox.sh" \
  && no "the sandbox never gets the Docker socket, privileges or the host network" || ok "the sandbox never gets the Docker socket, privileges or the host network"

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

f=$(fixture write-jobs 'jobs:
  shell:
    permissions:
      issues: write
    steps:
      - run: echo hi
  reader:
    permissions:
      contents: read
    steps:
      - uses: anthropics/claude-code-action@v1
  writer:
    runs-on: x
    permissions:
      contents: read
      pull-requests: write
    steps:
      - uses: anthropics/claude-code-action@v1
  minter:
    permissions: {}
    steps:
      - uses: actions/create-github-app-token@v2
      - uses: anthropics/claude-code-action@v1')
expect_read_only "a Claude job with a write permission or an App token is caught, a shell or read-only job isn't" 2 "$f"
expect_no_dispatch "of those, only the App token can dispatch" 1 "$f"

f=$(fixture pat-jobs 'jobs:
  publish:
    steps:
      - env:
          AUTOPILOT_TOKEN: ${{ secrets.AUTOPILOT_PAT }}
        run: echo hi
  build:
    permissions:
      contents: read
    steps:
      - uses: anthropics/claude-code-action@v1
      - env:
          T: ${{ secrets.AUTOPILOT_PAT }}
        run: echo hi')
expect_read_only "a Claude job that references AUTOPILOT_PAT is caught, a shell job isn't" 1 "$f"

f=$(fixture dispatch 'jobs:
  outcome:
    permissions:
      actions: write
    steps:
      - run: gh workflow run x.yml
  claude:
    permissions:
      contents: read
      pull-requests: write
      factions: write
    steps:
      - uses: anthropics/claude-code-action@v1
  both:
    permissions:
      contents: read
      actions: write
    steps:
      - uses: anthropics/claude-code-action@v1')
expect_no_dispatch "a Claude job with actions: write is caught, a shell job or other writes aren't" 1 "$f"

f=$(fixture bots $'jobs:\r
  a:\r
    steps:\r
      - uses: anthropics/claude-code-action@v1\r
        with:\r
          allowed_bots: "*"\r
      - uses: anthropics/claude-code-action@v1\r
        with:\r
          prompt: |\r
            allowed_bots: is not a setting in here\r
      - uses: some/action@v1\r
        with:\r
          allowed_bots: github-actions\r
  b:\r
    steps:\r
      - uses: anthropics/claude-code-action@v1\r
        with:\r
          allowed_bots: \'github-actions\'  # comment\r')
expect_bots "allowed_bots is read per Claude step, quoted or not (also with CRLF)" $'*\n-\ngithub-actions' "$f"

f=$(fixture runner-code $'jobs:\r
  test:\r
    steps:\r
      - run: dotnet build MusicHub.slnx\r
      - name: UI\r
        run: |\r
          echo hi\r
          pnpm --dir musichub-ui install\r
      - run: bash .github/scripts/sandbox.sh run "$X" 5 bash /work/trusted/sandbox-job.sh ci\r
      - name: Not a step\r
        env:\r
          NOTE: dotnet test is mentioned here\r
  probe:\r
    steps:\r
      - run: claude -p "x" --max-turns 1\r')
got=$(run_lines "$f" | grep -cE 'dotnet (restore|build|test)|pnpm ')
[ "$got" = 2 ] && ok "product code in a run step is caught, one-line or block, and an env value isn't" || no "product code in run steps (found $got, want 2)"
got=$(run_lines "$f" | grep -E '(^|[^a-z-])claude -p' | grep -cvF -- '--tools ""')
[ "$got" = 1 ] && ok "a claude run with tools on the runner is caught" || no "a claude run with tools on the runner (found $got, want 1)"

f=$(fixture pat-sandbox 'jobs:
  publish:
    steps:
      - env:
          T: ${{ secrets.AUTOPILOT_PAT }}
        run: bash .github/scripts/sandbox.sh run x 5 true
  build:
    steps:
      - run: bash .github/scripts/sandbox.sh run x 5 true
      - run: bash .github/scripts/sandbox.sh clean x
  fix:
    steps:
      - uses: actions/checkout@v4
        with:
          ref: ${{ inputs.sha }}
      - env:
          T: ${{ secrets.AUTOPILOT_BOARD_TOKEN }}
        run: echo hi')
got=$(comm -12 <(jobs_with 'secrets[.]AUTOPILOT_(PAT|BOARD_TOKEN)' "$f") <(jobs_with 'sandbox[.]sh run ' "$f") | grep -c .)
[ "$got" = 1 ] && ok "a job with the PAT that runs the sandbox is caught" || no "a job with the PAT that runs the sandbox (found $got, want 1)"
got=$(comm -12 <(jobs_with 'secrets[.]AUTOPILOT_(PAT|BOARD_TOKEN)' "$f") <(jobs_with 'ref:.*(inputs[.]sha|pull_request[.]head)' "$f") | grep -c .)
[ "$got" = 1 ] && ok "a job with the board token that checks out a PR is caught" || no "a job with the board token that checks out a PR (found $got, want 1)"
got=$(comm -23 <(jobs_with 'sandbox[.]sh run ' "$f") <(jobs_with 'sandbox[.]sh clean ' "$f") | grep -c .)
[ "$got" = 1 ] && ok "a job that leaves its sandbox behind is caught" || no "a job that leaves its sandbox behind (found $got, want 1)"
expect_read_only "a sandbox job with the PAT is caught as a Claude job with write access" 1 "$f"

echo "$PASS passed, $FAILED failed"
[ "$FAILED" -eq 0 ]
