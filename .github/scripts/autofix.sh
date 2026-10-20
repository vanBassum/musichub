#!/usr/bin/env bash
# The autofix controller for one PR of Claude's (a claude/ branch). autofix.yml runs
# it when the build or the reviewers finish on such a PR, and after every push to
# main (a PR may now conflict with it):
#
#   autofix.sh <pr>
#
# It reads only GitHub's own state: the PR, the newest check run per name on its
# head commit (the `build` job and the reviewers' "verdict · <reviewer>" checks from
# review-verdict.sh), and the Autofix runs of claude.yml, which it counts by their
# run name "Autofix #<pr>". Then it does at most one thing and prints why:
#
#   skip:  not a PR the autofix may touch
#   wait:  a check or an Autofix run hasn't finished yet
#   rerun: a reviewer left no verdict; rerun its review run
#   fix:   the PR conflicts with main, the build fails or a reviewer says FIX;
#          start an Autofix run of claude.yml for the head commit
#   stop:  a reviewer says STOP (a decision for Bas), or a limit is reached
#   done:  everything passed
#
# Environment: GITHUB_REPOSITORY, GH_TOKEN, and MERGEABLE_POLL_SECONDS (default 10;
# GitHub computes whether a PR conflicts some time after a push). Tested by
# .github/scripts/autofix.test.sh.
set -euo pipefail

MAX_AUTOFIX_RUNS=5
MAX_REVIEW_ATTEMPTS=3
MERGEABLE_POLLS=6
REVIEWERS=(api ui understanding smells)

fail() { echo "::error::$*" >&2; exit 1; }
# outcome <word> <reason>: report the decision and end.
outcome() {
  echo "$1: $2"
  [ -z "${GITHUB_STEP_SUMMARY:-}" ] || echo "PR #$pr: **$1**: $2" >>"$GITHUB_STEP_SUMMARY"
  exit 0
}

pr=${1:-}
[[ $pr =~ ^[1-9][0-9]*$ ]] || fail "Usage: autofix.sh <pr>"
[[ ${GITHUB_REPOSITORY:-} =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail "GITHUB_REPOSITORY is not set."
repo=$GITHUB_REPOSITORY

for ((poll = 1; ; poll++)); do
  pr_json=$(gh pr view "$pr" -R "$repo" --json state,isDraft,headRefName,headRefOid,mergeable,files) \
    || fail "Could not read PR #$pr."
  mergeable=$(jq -r .mergeable <<<"$pr_json")
  [ "$mergeable" = UNKNOWN ] && ((poll < MERGEABLE_POLLS)) || break
  sleep "${MERGEABLE_POLL_SECONDS:-10}"
done
sha=$(jq -r .headRefOid <<<"$pr_json")

[ "$(jq -r .state <<<"$pr_json")" = OPEN ] || outcome skip "the PR is not open"
[ "$(jq -r .isDraft <<<"$pr_json")" = false ] || outcome skip "the PR is a draft"
[[ $(jq -r .headRefName <<<"$pr_json") == claude/* ]] || outcome skip "not a claude/ branch"
jq -e '[.files[].path | select(test("^\\.(github|claude|githooks)/"))] | length == 0' <<<"$pr_json" >/dev/null \
  || outcome skip "the PR touches .github/, .claude/ or .githooks/, which Bas handles"

autofix_runs=$(gh run list -R "$repo" --workflow claude.yml --event workflow_dispatch --limit 200 \
  --json displayTitle,status) || fail "Could not read the Autofix runs."
autofix_runs=$(jq --arg title "Autofix #$pr" '[.[] | select(.displayTitle == $title)]' <<<"$autofix_runs")
jq -e 'all(.status == "completed")' <<<"$autofix_runs" >/dev/null \
  || outcome wait "an Autofix run for this PR is queued or running"
autofix_count=$(jq length <<<"$autofix_runs")

start_autofix() {
  ((autofix_count < MAX_AUTOFIX_RUNS)) \
    || outcome stop "$1, but this PR already had $autofix_count Autofix runs"
  gh workflow run claude.yml -R "$repo" --ref main -f pr="$pr" -f sha="$sha" \
    || fail "Could not start an Autofix run."
  outcome fix "$1 (Autofix run $((autofix_count + 1)) of $MAX_AUTOFIX_RUNS)"
}

# Checks run only on a PR that merges cleanly, so a conflict comes first.
[ "$mergeable" != CONFLICTING ] || start_autofix "the PR conflicts with main"

checks=$(gh api "repos/$repo/commits/$sha/check-runs" --paginate --jq '.check_runs[]' | jq -s .) \
  || fail "Could not read the checks of $sha."
# newest_check <name>: the newest check run with that name, or nothing.
newest_check() { jq -c --arg name "$1" '[.[] | select(.name == $name)] | max_by(.id) // empty' <<<"$checks"; }

build=$(newest_check build)
[ -n "$build" ] && [ "$(jq -r .status <<<"$build")" = completed ] || outcome wait "the build hasn't finished"
case $(jq -r .conclusion <<<"$build") in
  success) build_fails= ;;
  # The build job is skipped when a job it needs fails.
  failure | skipped | timed_out) build_fails=yes ;;
  *) outcome wait "the build didn't finish ($(jq -r .conclusion <<<"$build"))" ;;
esac

stops=() fixes=() no_verdict_runs=()
for reviewer in "${REVIEWERS[@]}"; do
  verdict=$(newest_check "verdict · $reviewer")
  [ -n "$verdict" ] || outcome wait "no verdict from $reviewer yet"
  case $(jq -r .external_id <<<"$verdict") in
    PASS) ;;
    FIX) fixes+=("$reviewer") ;;
    STOP) stops+=("$reviewer") ;;
    *) no_verdict_runs+=("$(jq -r .details_url <<<"$verdict" | sed -nE 's#.*/actions/runs/([0-9]+).*#\1#p')") ;;
  esac
done

((${#stops[@]} == 0)) || outcome stop "STOP from ${stops[*]}: a decision for Bas"

if ((${#no_verdict_runs[@]} > 0)); then
  run_id=${no_verdict_runs[0]}
  [ -n "$run_id" ] || fail "A verdict check has no review run link."
  attempts=$(gh api "repos/$repo/actions/runs/$run_id" --jq .run_attempt) || fail "Could not read run $run_id."
  ((attempts < MAX_REVIEW_ATTEMPTS)) || outcome stop "a reviewer left no verdict in $attempts attempts"
  gh run rerun "$run_id" --failed -R "$repo" || fail "Could not rerun run $run_id."
  outcome rerun "a reviewer left no verdict; rerunning review run $run_id"
fi

reasons=()
[ -z "$build_fails" ] || reasons+=("the build fails")
((${#fixes[@]} == 0)) || reasons+=("FIX from ${fixes[*]}")
if ((${#reasons[@]} > 0)); then
  printf -v reason '%s; ' "${reasons[@]}"
  start_autofix "${reason%; }"
fi

outcome "done" "the build and all reviewers passed $sha"
