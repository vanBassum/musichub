#!/usr/bin/env bash
# thread-markdown.sh issue|pr <repo> <number>: prints an issue or PR as markdown:
# number, title, author, labels (and branch, for a PR), its body, then every comment
# with its author and time. Autopilot puts it in the sandbox as issue.md or pr.md.
#
# Not `gh issue view --comments` or `gh pr view --comments`: without a terminal the
# issue one prints only the comments, without the title and body, and the PR one also
# reads the checks, which a job's read-only token may not. Needs gh and jq.
set -euo pipefail

kind=${1:-} repo=${2:-} n=${3:-}
case $kind in
  issue) fields=number,title,author,labels,body,comments ;;
  pr) fields=number,title,author,labels,headRefName,body,comments ;;
  *) echo "usage: thread-markdown.sh issue|pr <repo> <number>" >&2; exit 2 ;;
esac
[[ $n =~ ^[1-9][0-9]*$ ]] || { echo "Not a number: '$n'." >&2; exit 2; }
[ -n "$repo" ] || { echo "No repository given." >&2; exit 2; }

json=$(gh "$kind" view "$n" --repo "$repo" --json "$fields")
jq -r --arg kind "$kind" '
  "# \(if $kind == "pr" then "PR" else "Issue" end) #\(.number): \(.title)\n",
  "Author: \(.author.login)",
  "Labels: \([.labels[].name] | join(", ") | if . == "" then "none" else . end)",
  (if $kind == "pr" then "Branch: \(.headRefName)" else empty end),
  "",
  (.body // "" | if . == "" then "(no description)" else . end),
  "",
  "## Comments",
  (if (.comments | length) == 0 then "\n(none)" else empty end),
  (.comments[] | "\n### \(.author.login), \(.createdAt)\n\n\(.body)")
' <<<"$json"
