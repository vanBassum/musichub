#!/usr/bin/env bash
# One reviewer's verdict on the PR's head commit. The review job runs it after the
# reviewer, so that job is green only when the reviewer passed this exact commit:
#
#   review-verdict.sh <reviewer>
#
# A verdict is the marker a reviewer ends its summary comment with:
#   <!-- claude-review: <reviewer> sha=<SHA> blocking=<N> -->
# It fails with "N blocking finding(s)" (read the review comment) or with "No
# verdict" / "Malformed verdict" (rerun the job). Only comments by the workflow's
# own bot count, and after a rerun the newest verdict for the commit counts.
#
# Environment: GITHUB_REPOSITORY, PR, SHA (the full head SHA that was reviewed),
# GH_TOKEN. Tested by .github/scripts/review-verdict.test.sh.
set -euo pipefail

fail() { echo "::error::$*" >&2; exit 1; }

reviewer=${1:-}
[[ $reviewer =~ ^[a-z][a-z-]*$ ]] || fail "Usage: review-verdict.sh <reviewer>"
[[ ${GITHUB_REPOSITORY:-} =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail "GITHUB_REPOSITORY is not set."
[[ ${PR:-} =~ ^[1-9][0-9]*$ ]] || fail "PR is not a pull request number: '${PR:-}'."
[[ ${SHA:-} =~ ^[0-9a-f]{40}$ ]] || fail "SHA is not a full commit SHA: '${SHA:-}'."

comments=$(gh api "repos/$GITHUB_REPOSITORY/issues/$PR/comments" --paginate \
  --jq '.[] | select(.user.login == "github-actions[bot]" and .user.type == "Bot") | .body') \
  || fail "Could not read the PR's comments."
latest=$(grep -oE '<!--[^>]*claude-review[^>]*-->' <<<"$comments" | grep -F "$SHA" \
  | grep -E "claude-review:[[:space:]]*$reviewer([[:space:]]|-->)" | tail -n 1 || true)
[ -n "$latest" ] || fail "No verdict from $reviewer for $SHA. Rerun the job."
[[ $latest =~ ^\<\!--\ claude-review:\ $reviewer\ sha=$SHA\ blocking=(0|[1-9][0-9]{0,3})\ --\>$ ]] \
  || fail "Malformed verdict from $reviewer for $SHA: $latest. Rerun the job."
blocking=${BASH_REMATCH[1]}
[ "$blocking" = 0 ] || fail "$reviewer: $blocking blocking finding(s) for $SHA. See its review comment."
echo "$reviewer: no blocking findings for $SHA."
