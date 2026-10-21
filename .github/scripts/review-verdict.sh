#!/usr/bin/env bash
# One reviewer's verdict on the PR's head commit. The review job runs it after the
# reviewer, so that job is green only when the reviewer passed this exact commit:
#
#   review-verdict.sh <reviewer>
#
# The verdict is the reviewer's structured output (REVIEW_JSON, from the review
# step's `structured_output`; schema in claude-review.yml):
#   {"verdict": "PASS|FIX|STOP", "blocking": <N>, "findings": ["[tag] path:line — …", …]}
# The job reviewed exactly SHA, so the verdict belongs to that commit. It fails with
# "N blocking finding(s)" (read the review) or with "No verdict" / "Malformed
# verdict" (rerun the job).
#
# It publishes the verdict as the check run "verdict · <reviewer>" on that commit,
# for the autofix controller (autofix.sh) and the review report (review-report.sh).
# Its external_id is PASS, FIX, STOP or NONE (no valid verdict), and its summary
# holds the blocking findings, one "- " line each. PASS follows from blocking=0
# alone; otherwise the verdict says FIX or STOP, and a missing or contradicting
# verdict counts as STOP, so a person looks.
#
# Environment: GITHUB_REPOSITORY, SHA (the full head SHA that was reviewed),
# REVIEW_JSON, GH_TOKEN, GITHUB_SERVER_URL and GITHUB_RUN_ID for the check's link,
# and UNDERSTANDING_SHA (optional: the understanding commit that was checked,
# recorded in the check's text). Tested by .github/scripts/review-verdict.test.sh.
set -euo pipefail

fail() { echo "::error::$*" >&2; exit 1; }

reviewer=${1:-}
[[ $reviewer =~ ^[a-z][a-z-]*$ ]] || fail "Usage: review-verdict.sh <reviewer>"
[[ ${GITHUB_REPOSITORY:-} =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail "GITHUB_REPOSITORY is not set."
[[ ${SHA:-} =~ ^[0-9a-f]{40}$ ]] || fail "SHA is not a full commit SHA: '${SHA:-}'."
run_url="${GITHUB_SERVER_URL:-https://github.com}/$GITHUB_REPOSITORY/actions/runs/${GITHUB_RUN_ID:-0}"
text=
[ -z "${UNDERSTANDING_SHA:-}" ] || text="Checked against musichub-understanding $UNDERSTANDING_SHA."

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
    -f "output[title]=$2" -f "output[summary]=$3" ${text:+-f "output[text]=$text"} >/dev/null \
    || fail "Could not publish the verdict check for $reviewer."
}

no_verdict() {
  echo "::error::$1" >&2
  publish_verdict NONE "No verdict" "$1"
  exit 1
}

[ -n "${REVIEW_JSON:-}" ] || no_verdict "No verdict from $reviewer for $SHA. Rerun the job."
jq -e '
  type == "object"
  and (.verdict | IN("PASS", "FIX", "STOP"))
  and (.blocking | type == "number" and . == floor and . >= 0 and . < 10000)
  and (.findings | type == "array" and all(type == "string"))
' <<<"$REVIEW_JSON" >/dev/null 2>&1 \
  || no_verdict "Malformed verdict from $reviewer for $SHA. Rerun the job."
blocking=$(jq -r .blocking <<<"$REVIEW_JSON")

if [ "$blocking" = 0 ]; then
  publish_verdict PASS "PASS" "No blocking findings."
  echo "$reviewer: no blocking findings for $SHA."
  exit 0
fi

word=$(jq -r .verdict <<<"$REVIEW_JSON")
findings=$(jq -r '.findings[] | gsub("[\r\n]+"; " ") | "- " + ltrimstr("- ")' <<<"$REVIEW_JSON" | head -c 60000)
case $word in
  FIX | STOP) verdict=$word ;;
  *) verdict=STOP; findings="The verdict doesn't say FIX or STOP. See the review run."$'\n\n'"$findings" ;;
esac
publish_verdict "$verdict" "$verdict — $blocking blocking finding(s)" "${findings:-See the review run.}"
fail "$reviewer: $blocking blocking finding(s) for $SHA. See its inline comments and the review report."
