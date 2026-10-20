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
# It also publishes the verdict as the check run "verdict · <reviewer>" on that
# commit, for the autofix controller (autofix.sh). Its external_id is PASS, FIX,
# STOP or NONE (no valid verdict), and its summary holds the blocking findings.
# PASS follows from blocking=0 alone; otherwise the summary's title says FIX or
# STOP, and a missing or contradicting title counts as STOP, so a person looks.
#
# Environment: GITHUB_REPOSITORY, PR, SHA (the full head SHA that was reviewed),
# GH_TOKEN, and GITHUB_SERVER_URL and GITHUB_RUN_ID for the check's link. Tested by
# .github/scripts/review-verdict.test.sh.
set -euo pipefail

fail() { echo "::error::$*" >&2; exit 1; }

reviewer=${1:-}
[[ $reviewer =~ ^[a-z][a-z-]*$ ]] || fail "Usage: review-verdict.sh <reviewer>"
[[ ${GITHUB_REPOSITORY:-} =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail "GITHUB_REPOSITORY is not set."
[[ ${PR:-} =~ ^[1-9][0-9]*$ ]] || fail "PR is not a pull request number: '${PR:-}'."
[[ ${SHA:-} =~ ^[0-9a-f]{40}$ ]] || fail "SHA is not a full commit SHA: '${SHA:-}'."
run_url="${GITHUB_SERVER_URL:-https://github.com}/$GITHUB_REPOSITORY/actions/runs/${GITHUB_RUN_ID:-0}"

# publish_verdict <PASS|FIX|STOP|NONE> <title> <summary>
publish_verdict() {
  local conclusion
  case $1 in
    PASS) conclusion=success ;;
    STOP) conclusion=action_required ;;
    *) conclusion=failure ;;
  esac
  gh api "repos/$GITHUB_REPOSITORY/check-runs" --method POST \
    -f name="verdict · $reviewer" -f head_sha="$SHA" -f status=completed \
    -f conclusion="$conclusion" -f external_id="$1" -f details_url="$run_url" \
    -f "output[title]=$2" -f "output[summary]=$3" >/dev/null \
    || fail "Could not publish the verdict check for $reviewer."
}

no_verdict() {
  echo "::error::$1" >&2
  publish_verdict NONE "No verdict" "$1"
  exit 1
}

comments=$(gh api "repos/$GITHUB_REPOSITORY/issues/$PR/comments" --paginate \
  --jq '.[] | select(.user.login == "github-actions[bot]" and .user.type == "Bot") | .body') \
  || no_verdict "Could not read the PR's comments. Rerun the job."
comments=${comments//$'\r'/}
latest=$(grep -oE '<!--[^>]*claude-review[^>]*-->' <<<"$comments" | grep -F "$SHA" \
  | grep -E "claude-review:[[:space:]]*$reviewer([[:space:]]|-->)" | tail -n 1 || true)
[ -n "$latest" ] || no_verdict "No verdict from $reviewer for $SHA. Rerun the job."
[[ $latest =~ ^\<\!--\ claude-review:\ $reviewer\ sha=$SHA\ blocking=(0|[1-9][0-9]{0,3})\ --\>$ ]] \
  || no_verdict "Malformed verdict from $reviewer for $SHA: $latest. Rerun the job."
blocking=${BASH_REMATCH[1]}

if [ "$blocking" = 0 ]; then
  publish_verdict PASS "PASS" "No blocking findings."
  echo "$reviewer: no blocking findings for $SHA."
  exit 0
fi

# The summary comment that ends in this marker: its title's verdict word, then its
# findings without hidden lines. A marker line ends any comment before it.
summary=$(awk -v title_re="^### (PASS|FIX|STOP) — $reviewer\$" -v marker="$latest" '
  $0 == marker { if (title != "") { word = title; sub(/^### /, "", word); sub(/ .*/, "", word); found = word "\n" body } }
  /^<!--/ { if ($0 ~ /claude-review:/) { title = ""; body = "" } next }
  $0 ~ title_re { title = $0; body = ""; next }
  title != "" { body = body $0 "\n" }
  END { printf "%s", found }
' <<<"$comments")
word=$(head -n 1 <<<"$summary")
findings=$(tail -n +2 <<<"$summary" | head -c 60000)
case $word in
  FIX | STOP) verdict=$word ;;
  *) verdict=STOP; findings="The summary's title doesn't say FIX or STOP. See the review comment."$'\n\n'"$findings" ;;
esac
publish_verdict "$verdict" "$verdict — $blocking blocking finding(s)" "${findings:-See the review comment.}"
fail "$reviewer: $blocking blocking finding(s) for $SHA. See its review comment."
