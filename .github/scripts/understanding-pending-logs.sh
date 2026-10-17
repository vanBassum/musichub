#!/usr/bin/env bash
set -euo pipefail

repo="${1:-understanding}"
output="${2:-understanding-pending-logs.md}"
projections=(model/mental-model.md features/features.md architecture/architecture.md security/security.md)

cd "$repo"
git rev-parse --verify HEAD >/dev/null

# Choose the oldest last-change commit of the four projections. Using an
# earlier boundary may repeat accepted logs but must never omit them.
oldest=""
oldest_distance=-1
for path in "${projections[@]}"; do
  if [[ ! -f "$path" ]]; then
    echo "Missing projection: $path" >&2
    exit 1
  fi
  commit="$(git log -1 --format=%H HEAD -- "$path")"
  if [[ -z "$commit" ]]; then
    echo "No history for projection: $path" >&2
    exit 1
  fi
  distance="$(git rev-list --count "${commit}..HEAD")"
  if (( distance > oldest_distance )); then
    oldest="$commit"
    oldest_distance="$distance"
  fi
done

mapfile -t changed < <(git diff --diff-filter=A --name-only "$oldest" HEAD -- logs/ | LC_ALL=C sort)
{
  printf '# Accepted logs not yet guaranteed in every projection\n\n'
  printf 'Understanding HEAD: `%s`\n\n' "$(git rev-parse HEAD)"
  printf 'Conservative projection boundary: `%s`\n\n' "$oldest"
  printf 'These are newly added log files after the oldest projection update. Read alongside the projections; later accepted decisions supersede earlier ones.\n\n'
  if (( ${#changed[@]} == 0 )); then
    printf 'No newly added logs detected.\n'
  fi
  for path in "${changed[@]}"; do
    [[ "$path" == logs/*.md ]] || continue
    printf '\n---\n\n## %s\n\n' "$path"
    cat -- "$path"
    printf '\n'
  done
} > "../$output"
