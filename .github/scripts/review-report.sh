#!/usr/bin/env bash
# The review report: one PR comment that shows every reviewer's verdict on the PR's
# head commit. The review workflow runs it once all reviewers are done:
#
#   review-report.sh
#
# It reads the newest "verdict · <reviewer>" check run per reviewer on SHA (the
# check runs review-verdict.sh publishes, which stay the machine-readable source for
# the autofix and the merge gate) and writes them into the one comment that ends in
# the report marker, editing it when it exists. It writes nothing when SHA is no
# longer the PR's head, so a run for an older commit never replaces a newer report.
#
# Environment: GITHUB_REPOSITORY, PR, SHA (the full head SHA that was reviewed),
# GH_TOKEN. Tested by .github/scripts/review-report.test.sh.
set -euo pipefail

MARKER='<!-- claude-review-report -->'
MAX_SUMMARY_CHARS=12000

fail() { echo "::error::$*" >&2; exit 1; }

[[ ${GITHUB_REPOSITORY:-} =~ ^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$ ]] || fail "GITHUB_REPOSITORY is not set."
[[ ${PR:-} =~ ^[1-9][0-9]*$ ]] || fail "PR is not a pull request number: '${PR:-}'."
[[ ${SHA:-} =~ ^[0-9a-f]{40}$ ]] || fail "SHA is not a full commit SHA: '${SHA:-}'."
repo=$GITHUB_REPOSITORY

head_is_sha() {
  local head
  head=$(gh api "repos/$repo/pulls/$PR" --jq .head.sha) || fail "Could not read PR #$PR."
  [ "$head" = "$SHA" ]
}
head_is_sha || { echo "skip: $SHA is no longer the head of PR #$PR"; exit 0; }

checks=$(gh api "repos/$repo/commits/$SHA/check-runs" --paginate --jq '.check_runs[]' | jq -s .) \
  || fail "Could not read the checks of $SHA."
verdicts=$(jq -c '[.[] | select(.name | startswith("verdict · "))] | group_by(.name) | map(max_by(.id))' <<<"$checks")

body="## Claude review"$'\n\n'"Verdicts for \`${SHA:0:12}\`. The findings are also inline on the changed lines."$'\n'
while IFS= read -r verdict; do
  reviewer=$(jq -r '.name | sub("^verdict · "; "")' <<<"$verdict")
  word=$(jq -r '.external_id | if . == "NONE" then "No verdict" else . end' <<<"$verdict")
  body+=$'\n'"### $word — $reviewer"$'\n'
  [ "$word" = PASS ] && continue
  summary=$(jq -r '.output.summary // ""' <<<"$verdict")
  if ((${#summary} > MAX_SUMMARY_CHARS)); then
    summary="${summary:0:MAX_SUMMARY_CHARS}"$'\n\n'"… (cut short: the full list is in the check)"
  fi
  body+=$'\n'"$summary"$'\n\n'"[Review run]($(jq -r .details_url <<<"$verdict"))"$'\n'
done < <(jq -c '.[]' <<<"$verdicts")
[ "$verdicts" != '[]' ] || body+=$'\n'"No verdicts yet."$'\n'
body+=$'\n'"$MARKER"

report_ids=$(gh api "repos/$repo/issues/$PR/comments" --paginate \
  --jq ".[] | select(.user.login == \"github-actions[bot]\" and (.body | contains(\"$MARKER\"))) | .id") \
  || fail "Could not read the PR's comments."
existing=${report_ids%%$'\n'*}

# Checked again right before writing: a newer run may have finished meanwhile.
head_is_sha || { echo "skip: $SHA is no longer the head of PR #$PR"; exit 0; }
if [ -n "$existing" ]; then
  jq -n --arg body "$body" '{body: $body}' \
    | gh api --method PATCH "repos/$repo/issues/comments/$existing" --input - >/dev/null \
    || fail "Could not update the review report."
  echo "updated the review report for $SHA"
else
  jq -n --arg body "$body" '{body: $body}' \
    | gh api --method POST "repos/$repo/issues/$PR/comments" --input - >/dev/null \
    || fail "Could not post the review report."
  echo "posted the review report for $SHA"
fi
