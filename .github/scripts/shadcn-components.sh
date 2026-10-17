#!/usr/bin/env bash
# Adds the shadcn/ui components that autopilot's analysis admitted, before Claude's
# build starts, and commits them. Run from the repository root, with the UI's
# dependencies installed:
#
#   bash .github/scripts/shadcn-components.sh <analysis.md> <result.md>
#
# The analysis names them on one line, `Components: table, checkbox` (or `none`).
# Claude never runs the shadcn CLI: its build has no network and no package
# commands. This step runs it instead, and only within these limits:
#
# - names only: lowercase names from the official registry, so no URL, path or
#   other registry can be named;
# - no package: every npm package the components (and the components they pull in)
#   need must already be in musichub-ui/package.json. The registry is read with
#   `shadcn view` first, so the CLI never gets to install a package; afterwards
#   package.json and pnpm-lock.yaml must be unchanged as well;
# - only new files under musichub-ui/src/components/ui/.
#
# Anything else refuses: it writes <result.md> as BLOCKED with the reason (publish
# then stops the attempt and comments it on the issue), leaves the tree clean and
# exits 0, so the build job doesn't fail as if Claude had crashed. Claude's build
# step skips itself when <result.md> exists. A new package needs Bas's approval
# and a human-authored PR first.
#
# Tested by .github/scripts/shadcn-components.test.sh (with a stub pnpm).
set -euo pipefail

ANALYSIS=$1
RESULT=$2
UI=musichub-ui
UI_DIR=$UI/src/components/ui
NAME_RE='^[a-z][a-z0-9-]{0,39}$'
MAX=10

refuse() {
  git reset -q --hard
  git clean -fdq -- "$UI/src"
  printf 'BLOCKED\nBefore the build started, adding the shadcn/ui components the analysis admitted was refused: %s\n\nNothing was built. %s\n' \
    "$1" "${2:-}" >"$RESULT"
  echo "::warning::Components refused: $1"
  exit 0
}

lines=$(grep -E '^Components:' "$ANALYSIS" | tr -d '\r' || true)
[ -n "$lines" ] || { echo "The analysis names no components."; exit 0; }
[ "$(grep -c . <<<"$lines")" -eq 1 ] || refuse "the analysis has more than one \`Components:\` line."
list=$(sed -E 's/^Components:[[:space:]]*//; s/[[:space:]]+$//' <<<"$lines")
[ "$list" = none ] && { echo "The analysis names no components."; exit 0; }

wanted=()
IFS=',' read -ra parts <<<"$list"
for part in "${parts[@]}"; do
  name=$(tr -d '[:space:]`' <<<"$part")
  [[ $name =~ $NAME_RE ]] || refuse "\`$(tr -d '`' <<<"$part" | head -c 60)\` is not the name of a component in the official registry."
  [ -e "$UI_DIR/$name.tsx" ] && { echo "$name is already in $UI_DIR."; continue; }
  wanted+=("$name")
done
[ "${#wanted[@]}" -le "$MAX" ] || refuse "the analysis names more than $MAX components."
[ "${#wanted[@]}" -gt 0 ] || { echo "Every admitted component is already there."; exit 0; }

# The packages the UI already has, by name.
have=$(jq -r '(.dependencies // {}) + (.devDependencies // {}) | keys[]' "$UI/package.json" | tr -d '\r')

# Walk the components and the ones they pull in (registryDependencies), reading
# each from the registry without writing anything.
queue=("${wanted[@]}") seen=" "
while [ "${#queue[@]}" -gt 0 ]; do
  name=${queue[0]}; queue=("${queue[@]:1}")
  case $seen in *" $name "*) continue ;; esac
  seen="$seen$name "
  item=$(pnpm --dir "$UI" exec shadcn view "$name" 2>/dev/null) \
    || refuse "the registry has no component \`$name\` (or could not be read)."
  jq -e 'type == "array" and length == 1' <<<"$item" >/dev/null \
    || refuse "the registry returned something unexpected for \`$name\`."
  while IFS= read -r dep; do
    [ -n "$dep" ] || continue
    pkg=$(sed -E 's/^(@?[^@]+)@.*$/\1/' <<<"$dep")
    grep -qxF -- "$pkg" <<<"$have" \
      || refuse "\`$name\` needs the npm package \`$pkg\`, which musichub-ui doesn't have." \
        "A new package needs Bas's approval and a human-authored PR that adds it first."
  done < <(jq -r '.[0] | ((.dependencies // []) + (.devDependencies // []))[]' <<<"$item" | tr -d '\r')
  while IFS= read -r sub; do
    [ -n "$sub" ] || continue
    [[ $sub =~ $NAME_RE ]] || refuse "\`$name\` pulls in \`$(head -c 60 <<<"$sub")\`, which is not a component of the official registry."
    [ -e "$UI_DIR/$sub.tsx" ] || queue+=("$sub")
  done < <(jq -r '.[0].registryDependencies // [] | .[]' <<<"$item" | tr -d '\r')
done

pnpm --dir "$UI" exec shadcn add "${wanted[@]}" --yes \
  || refuse "\`shadcn add ${wanted[*]}\` failed."

git diff --quiet HEAD -- "$UI/package.json" "$UI/pnpm-lock.yaml" \
  || refuse "\`shadcn add\` changed package.json or pnpm-lock.yaml." \
    "A new package needs Bas's approval and a human-authored PR that adds it first."
changed=$(git status --porcelain --untracked-files=all | cut -c4-)
outside=$(grep -vE "^$UI_DIR/[a-z0-9-]+\.tsx$" <<<"$changed" || true)
[ -z "$outside" ] || refuse "\`shadcn add\` changed files outside $UI_DIR: $(tr '\n' ' ' <<<"$outside")"
modified=$(git status --porcelain -- "$UI_DIR" | grep -v '^??' || true)
[ -z "$modified" ] || refuse "\`shadcn add\` changed components that were already there: $(cut -c4- <<<"$modified" | tr '\n' ' ')"
[ -n "$changed" ] || refuse "\`shadcn add ${wanted[*]}\` added no files."

pnpm --dir "$UI" exec prettier --write $(sed "s|^$UI/||" <<<"$changed") >/dev/null
git add -- $changed
git commit -q -m "Add the shadcn/ui components the analysis admitted: $(tr '\n' ' ' <<<"$changed" | sed -E "s|$UI_DIR/||g; s|\.tsx||g; s/ +$//; s/ /, /g")"
echo "Added and committed: $(tr '\n' ' ' <<<"$changed")"
