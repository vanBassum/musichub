#!/usr/bin/env bash
# What runs inside the sandbox container (see sandbox.sh), in /work/repo. The job's
# trusted steps copy this script and the prompts from main to /work/trusted, so
# nothing in the repository under work can change them.
#
#   sandbox-job.sh ci      the PR checks: restore, build and test the API, lint and
#                          build the UI. Fails on the first step that fails.
#   sandbox-job.sh build   autopilot's build: Claude judges the issue and, when it
#                          may be built, builds it (/work/trusted/build.md)
#   sandbox-job.sh fix     autopilot's fix round: main merged in first, then Claude
#                          fixes the review findings (/work/trusted/fix.md)
#
# build and fix leave their results in /work/out: Claude's result.md, base (the
# commit the run started from), validation (`pass` or why not), autopilot.bundle
# (the new commits), claude-exit and usage-<job>.json. They never fail on what
# Claude did; the job's publish step judges that. They fail only when the sandbox
# itself breaks, so the job fails and the watchdog retries it once.
#
# Parameters, from the job: SANDBOX_REPO, SANDBOX_ISSUE, SANDBOX_JUDGE_ONLY (build),
# SANDBOX_PR and SANDBOX_ROUND (fix).
set -euo pipefail

OUT=/work/out
TRUSTED=/work/trusted
# Every Claude run of autopilot (build and fix) has exactly this permission policy.
# In an unattended run --allowedTools is all of it: anything not listed is refused,
# since nobody can approve it. Files are created and changed with Write and Edit,
# moved with `git mv`, deleted with `git rm`. shadcn-components.sh and
# add-package.sh are the only ways to add a component or a package. There is no
# GitHub access at all: the job's trusted steps put the issue, the PR and the
# findings in /work/in. No test suite either: the understanding has implementation
# agents address test failures from CI results (architecture, "Delivery pipeline"),
# so the pipeline runs the tests (validate, and the build check) and a failure comes
# back to Claude as a fix round. .github/scripts/workflows.test.sh pins this list.
TOOLS="Read,Write,Edit,Grep,Glob,Bash(dotnet restore:*),Bash(dotnet build:*),Bash(dotnet ef:*),Bash(pnpm --dir musichub-ui lint:*),Bash(pnpm --dir musichub-ui typecheck:*),Bash(pnpm --dir musichub-ui build:*),Bash(pnpm --dir musichub-ui format:*),Bash(git add:*),Bash(git commit:*),Bash(git mv:*),Bash(git rm:*),Bash(git status:*),Bash(git diff:*),Bash(git log:*),Bash(git show:*),Bash(bash /work/trusted/shadcn-components.sh:*),Bash(bash /work/trusted/add-package.sh:*)"
BUILD_TURNS=200
FIX_TURNS=120

step() { echo "::group::$*"; }
end() { echo "::endgroup::"; }

setup_git() {
  git config --global user.name "musichub autopilot"
  git config --global user.email "autopilot@musichub.invalid"
  git config --global safe.directory /work/repo
}

restore() {
  step "Restore"
  dotnet restore MusicHub.slnx
  pnpm --dir musichub-ui install --frozen-lockfile
  end
}

# For Claude's runs a failed restore is not the end: it may be what Claude has to
# fix (a package a fix round has to correct), and Claude can restore again itself.
restore_for_claude() {
  if ! (restore); then
    echo "::warning::The restore failed; Claude starts anyway."
    end
  fi
}

cmd_ci() {
  restore
  step "Build the API"; dotnet build MusicHub.slnx --configuration Release --no-restore; end
  step "Test the API"; dotnet test MusicHub.slnx --configuration Release --no-build --no-restore; end
  step "Lint the UI"; pnpm --dir musichub-ui lint; end
  step "Build the UI"; pnpm --dir musichub-ui build; end
}

# claude_run <job> <turns> <prompt>: one unattended Claude Code run, its stream kept
# in /work/out. Its exit status is recorded, not passed on.
claude_run() {
  local job=$1 turns=$2 prompt=$3 code=0
  step "Claude $job"
  claude -p "$prompt" --max-turns "$turns" \
    --add-dir /work/in --add-dir "$OUT" --add-dir "$TRUSTED" \
    --allowedTools "$TOOLS" --strict-mcp-config --no-session-persistence \
    --output-format stream-json --verbose >"$OUT/claude-$job.jsonl" 2>"$OUT/claude-$job.err" || code=$?
  echo "$code" >"$OUT/claude-exit"
  bash "$TRUSTED/claude-usage.sh" "$OUT/claude-$job.jsonl" >"$OUT/usage-$job.json" 2>/dev/null || rm -f "$OUT/usage-$job.json"
  echo "Claude exited with $code."
  end
}

# validate: independent of what Claude reported, a clean tree, and the same checks
# as CI, the tests included. Recorded in /work/out/validation: `pass`, or `fail:`
# and the first check that failed. publish stops on uncommitted changes; a failed
# build, test or lint still becomes a PR, whose build check fails the same way and
# gets a fix round from its results.
validate() {
  local v=pass
  step "Validate"
  if [ -n "$(git status --porcelain)" ]; then v="fail: Claude left uncommitted changes"
  elif ! dotnet build MusicHub.slnx --configuration Release; then v="fail: dotnet build MusicHub.slnx --configuration Release failed"
  elif ! dotnet test MusicHub.slnx --configuration Release --no-build; then v="fail: dotnet test MusicHub.slnx failed"
  elif ! pnpm --dir musichub-ui lint; then v="fail: pnpm --dir musichub-ui lint failed"
  elif ! pnpm --dir musichub-ui build; then v="fail: pnpm --dir musichub-ui build (tsc -b and vite build) failed"
  fi
  echo "$v" >"$OUT/validation"
  echo "Validation: $v"
  end
}

# finish <base>: validation when Claude reported READY with work to publish, and
# the bundle of every commit since <base>.
finish() {
  local base=$1
  if [ "$(head -n1 "$OUT/result.md" 2>/dev/null | tr -d '[:space:]')" = READY ] && [ "${SANDBOX_JUDGE_ONLY:-false}" != true ]; then
    validate
  else
    echo "skipped: Claude did not report READY" >"$OUT/validation"
  fi
  if [ "$(git rev-list --count "$base..HEAD")" -gt 0 ]; then
    git bundle create "$OUT/autopilot.bundle" "$base..HEAD"
  fi
}

cmd_build() {
  local base
  setup_git
  git switch -q -c autopilot-work
  base=$(git rev-parse HEAD); echo "$base" >"$OUT/base"
  restore_for_claude
  claude_run build "$BUILD_TURNS" "REPO: ${SANDBOX_REPO:?}
ISSUE: ${SANDBOX_ISSUE:?}
JUDGE_ONLY: ${SANDBOX_JUDGE_ONLY:-false}
IN_DIR: /work/in
OUT_DIR: $OUT

Read $TRUSTED/build.md and follow it exactly."
  finish "$base"
}

cmd_fix() {
  local base merge
  setup_git
  base=$(git rev-parse HEAD); echo "$base" >"$OUT/base"
  git switch -q -c autopilot-fix
  # The PR gets the newest main first: a conflict is part of the fix, and the
  # reviewers and CI see the PR as it would merge.
  step "Merge main"
  if ! git rev-parse -q --verify refs/remotes/origin/main >/dev/null; then
    merge="main could not be read, so it was not merged in."
  elif git merge-base --is-ancestor refs/remotes/origin/main HEAD; then
    merge="The PR is up to date with main."
  elif git merge -q --no-edit -m "Merge main into the PR" refs/remotes/origin/main; then
    merge="main was merged into the PR without conflicts."
  else
    merge="Merging main into the PR stopped on conflicts in these files. The merge is still in progress: resolve each conflict with Edit, then \`git add\` the files and \`git commit\` to conclude the merge, before anything else.

$(git diff --name-only --diff-filter=U | sed 's/^/- /')"
  fi
  printf '%s\n' "$merge" >"$OUT/merge.md"
  echo "$merge"
  end
  restore_for_claude
  claude_run fix "$FIX_TURNS" "REPO: ${SANDBOX_REPO:?}
ISSUE: ${SANDBOX_ISSUE:?}
PR: ${SANDBOX_PR:?}
ROUND: ${SANDBOX_ROUND:?}
IN_DIR: /work/in
OUT_DIR: $OUT

Read $TRUSTED/fix.md and follow it exactly."
  finish "$base"
}

case "${1:-}" in
  ci) cmd_ci ;;
  build) cmd_build ;;
  fix) cmd_fix ;;
  *) echo "Usage: sandbox-job.sh ci | build | fix" >&2; exit 2 ;;
esac
