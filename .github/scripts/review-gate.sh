#!/usr/bin/env bash
# Fail-closed merge gate for Claude-reviewed pull requests. The workflow runs it
# from the trusted base branch, never from the PR. Anything missing, unexpected or
# unreadable stops the merge; only an explicit, complete "yes" merges.
#
#   review-gate.sh verdict <reviewer>   print that reviewer's verdict for $SHA:
#                                       "<reviewer>: blocking=<N> decisions=<D> for <SHA>"
#   review-gate.sh merge                check every gate, then squash-merge $PR
#
# A verdict is the marker a reviewer ends its summary with:
#   <!-- claude-review: <reviewer> sha=<SHA> blocking=<N> decisions=<D> -->
# N counts its blocking findings. D, of those, the ones that need a decision rather
# than a code change (UNDOCUMENTED, CONTRADICTED, or a change autopilot may not make
# on its own); autopilot gives those no fix round. Only the understanding reviewer
# reports decisions, and its marker must have the field; for the others it may be
# left out, and is then 0.
#
# A refusal is also written to $GITHUB_OUTPUT as `reason`, so the autopilot job can
# say why the gate refused.
# Environment: GITHUB_REPOSITORY, PR, SHA (the full head SHA that was reviewed),
# and for `merge` also REVIEWERS (space-separated) and MERGE_TOKEN. Optional: BASE_BRANCH (main),
# BUILD_CHECK (build), BUILD_WAIT_SECONDS (1200), POLL_SECONDS (20), and
# PIPELINE_MODE (an autopilot PR merges only when it is `full`; see autopilot.sh).
#
# Everything is read with GH_TOKEN (the workflow's GITHUB_TOKEN); only the merge itself
# uses MERGE_TOKEN, the AUTOPILOT_PAT secret. A push made with GITHUB_TOKEN starts no
# workflows, so a merge with it would never be built or checked on main, and main
# would get no image. Without MERGE_TOKEN the gate doesn't merge at all.
#
# Tested by .github/scripts/review-gate.test.sh.
set -euo pipefail

fail() {
  echo "::error::$*" >&2
  [ -z "${GITHUB_OUTPUT:-}" ] || printf 'reason=%s\n' "$(printf '%s' "$*" | tr '\r\n' '  ' | cut -c1-500)" >>"$GITHUB_OUTPUT"
  exit 1
}

[[ ${GITHUB_REPOSITORY:-} =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail "GITHUB_REPOSITORY is not set."
[[ ${PR:-} =~ ^[1-9][0-9]*$ ]] || fail "PR is not a pull request number: '${PR:-}'."
[[ ${SHA:-} =~ ^[0-9a-f]{40}$ ]] || fail "SHA is not a full commit SHA: '${SHA:-}'."
API="repos/$GITHUB_REPOSITORY"

# Bodies of the comments the review jobs posted (as the workflow's GITHUB_TOKEN).
bot_comments() {
  gh api "$API/issues/$PR/comments" --paginate \
    --jq '.[] | select(.user.login == "github-actions[bot]" and .user.type == "Bot") | .body'
}

# Each reviewer may post a new verdict for the same SHA after a full rerun.
# GitHub returns issue comments oldest-first. Use only the newest bot-authored
# comment containing this reviewer's exact-SHA marker. A malformed newest
# verdict fails closed; a new run cannot silently reuse an older approval.
# Prints "<blocking> <decisions>".
verdict_from() {
  local comments=$1 reviewer=$2 markers latest count decisions
  [[ $reviewer =~ ^[a-z][a-z-]*$ ]] || fail "Bad reviewer name '$reviewer'."
  markers=$(grep -oE '<!--[^>]*claude-review[^>]*-->' <<<"$comments" \
    | grep -F "$SHA" | grep -E "claude-review:[[:space:]]*$reviewer([[:space:]]|-->)" || true)
  [ -n "$markers" ] || fail "No verdict from $reviewer for $SHA."
  latest=$(tail -n 1 <<<"$markers")
  [[ $latest =~ ^\<\!--\ claude-review:\ $reviewer\ sha=$SHA\ blocking=([0-9]+)(\ decisions=([0-9]+))?\ --\>$ ]] \
    || fail "Malformed verdict from $reviewer for $SHA: $latest"
  count="${BASH_REMATCH[1]}" decisions="${BASH_REMATCH[3]}"
  [[ $count =~ ^(0|[1-9][0-9]{0,3})$ ]] \
    || fail "Malformed verdict from $reviewer for $SHA: $latest"
  if [ -z "$decisions" ]; then
    [ "$reviewer" != understanding ] || fail "Malformed verdict from $reviewer for $SHA (it has no decisions=): $latest"
    decisions=0
  fi
  [[ $decisions =~ ^(0|[1-9][0-9]{0,3})$ ]] && [ "$decisions" -le "$count" ] \
    || fail "Malformed verdict from $reviewer for $SHA (decisions can't exceed blocking): $latest"
  echo "$count $decisions"
}

refuse_if_labelled() {
  local labels
  labels=$(gh api "$API/issues/$PR/labels" --paginate --jq '.[].name')
  if grep -qx 'no-automerge' <<<"$labels"; then
    fail "Labelled no-automerge: not merging; merge it by hand when ready."
  fi
}

# An autopilot PR (branch autopilot/…) merges only while it is its issue's active
# attempt: once the attempt is stopped or superseded, no rerun or late review can
# merge it. autopilot.sh owns that state; other PRs pass straight through.
refuse_inactive_autopilot() {
  bash "$(dirname "${BASH_SOURCE[0]}")/autopilot.sh" gate || fail "Not merging this autopilot PR."
}

check_pr_state() {
  local state open draft head base files
  state=$(gh api "$API/pulls/$PR" --jq '[.state, .draft, .head.sha, .base.ref, .changed_files] | @tsv')
  read -r open draft head base files <<<"$state"
  [ "$open" = open ] || fail "PR is $open."
  [ "$draft" = false ] || fail "PR is a draft."
  [ "$head" = "$SHA" ] || fail "PR head moved to $head; this run reviewed $SHA."
  [ "$base" = "${BASE_BRANCH:-main}" ] || fail "PR targets $base, not ${BASE_BRANCH:-main}."
  [[ $files =~ ^[0-9]+$ ]] || fail "Could not read the PR's changed-file count."
  echo "$files"
}

# Changes to the review mechanism (workflows, reviewer rules, this gate) or to the
# guards on each machine (.githooks/, .claude/) never merge themselves: the
# mechanism that decides merges can't approve its own change.
refuse_guarded_paths() {
  local expected=$1 listing names guarded
  listing=$(gh api "$API/pulls/$PR/files" --paginate --jq '.[] | [.filename, (.previous_filename // "")] | @tsv')
  [ "$(grep -c . <<<"$listing" || true)" -eq "$expected" ] \
    || fail "Listed $(grep -c . <<<"$listing" || true) of $expected changed files; can't check them all."
  names=$(tr '\t' '\n' <<<"$listing" | grep . || true)
  guarded=$(grep -E '^\.(github|githooks|claude)/' <<<"$names" | sort -u || true)
  if [ -n "$guarded" ]; then
    echo "Changes the review mechanism or the local guards:"; echo "$guarded"
    fail "This PR changes .github/, .githooks/ or .claude/; reviewers passed, but Bas merges it by hand."
  fi
}

# Every latest check run named $BUILD_CHECK from GitHub Actions on $SHA must
# succeed; none, a failure, or no result in time all refuse.
wait_for_build() {
  local name=${BUILD_CHECK:-build} deadline=$((SECONDS + ${BUILD_WAIT_SECONDS:-1200})) runs bad
  while :; do
    runs=$(gh api "$API/commits/$SHA/check-runs?check_name=$name&filter=latest&per_page=100" \
      --jq '.check_runs[] | select(.app.slug == "github-actions") | "\(.status) \(.conclusion)"')
    if [ -n "$runs" ] && ! grep -qv '^completed ' <<<"$runs"; then
      bad=$(grep -vx 'completed success' <<<"$runs" || true)
      [ -z "$bad" ] || fail "Check '$name' did not pass: $(tr '\n' ' ' <<<"$bad")"
      echo "Check '$name' passed."
      return
    fi
    ((SECONDS < deadline)) || fail "Check '$name' did not finish in time: ${runs:-no run found}."
    sleep "${POLL_SECONDS:-20}"
  done
}

cmd_verdict() {
  local reviewer=${1:-} comments v n d
  comments=$(bot_comments)
  v=$(verdict_from "$comments" "$reviewer")
  read -r n d <<<"$v"
  echo "$reviewer: blocking=$n decisions=$d for $SHA"
}

cmd_merge() {
  local files comments r v n
  [ -n "${REVIEWERS// /}" ] || fail "REVIEWERS is empty."
  [ -n "${MERGE_TOKEN:-}" ] || fail "No MERGE_TOKEN (the AUTOPILOT_PAT secret): a merge with GITHUB_TOKEN would start no workflow, so main would get no build and no image. Not merging."
  refuse_if_labelled
  refuse_inactive_autopilot
  files=$(check_pr_state)
  comments=$(bot_comments)
  for r in $REVIEWERS; do
    v=$(verdict_from "$comments" "$r")
    read -r n _ <<<"$v"
    [ "$n" = 0 ] || fail "$r reported $n blocking finding(s) for $SHA."
    echo "$r: blocking=0"
  done
  refuse_guarded_paths "$files"
  wait_for_build
  # The wait can be long: look again right before merging.
  refuse_if_labelled
  refuse_inactive_autopilot
  check_pr_state >/dev/null
  GH_TOKEN=$MERGE_TOKEN gh pr merge "$PR" --repo "$GITHUB_REPOSITORY" --squash --delete-branch --match-head-commit "$SHA"
}

case "${1:-}" in
  verdict) shift; cmd_verdict "$@" ;;
  merge) cmd_merge ;;
  *) fail "Usage: review-gate.sh verdict <reviewer> | merge" ;;
esac
