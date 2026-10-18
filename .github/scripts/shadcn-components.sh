#!/usr/bin/env bash
# The only way autopilot's Claude adds shadcn/ui components. It runs in the sandbox
# (see sandbox-job.sh), from the repository root, with the UI's dependencies
# installed, and commits the components it adds:
#
#   bash /work/trusted/shadcn-components.sh <name> [<name>...]    (or comma separated)
#
# The official shadcn CLI adds them, and only within these limits:
#
# - names only: lowercase names from the official registry, so no URL, path or
#   other registry can be named;
# - no package: every npm package the components (and the components they pull in)
#   need must already be in musichub-ui/package.json. The registry is read with
#   `shadcn view` first, so the CLI never gets to install a package; afterwards
#   package.json and pnpm-lock.yaml must be unchanged as well. A package a
#   component needs is added first, on its own, with add-package.sh, when the
#   admission rules allow it;
# - only new files under musichub-ui/src/components/ui/;
# - a clean tree to start from, so that what the CLI wrote is all there is to check
#   and to undo.
#
# Anything else refuses: it prints `REFUSED: <reason>`, leaves the tree as it was
# and exits 3.
#
# Tested by .github/scripts/shadcn-components.test.sh (with a stub pnpm).
set -euo pipefail

UI=musichub-ui
UI_DIR=$UI/src/components/ui
NAME_RE='^[a-z][a-z0-9-]{0,39}$'
MAX=10

refuse() {
  if [ -n "${STARTED:-}" ]; then
    git reset -q --hard
    git clean -fdq -- "$UI/src"
  fi
  echo "REFUSED: $1${2:+ $2}"
  exit 3
}

[ $# -gt 0 ] || refuse "name at least one component."
[ -z "$(git status --porcelain --untracked-files=all)" ] || refuse "commit your work first: shadcn-components.sh needs a clean tree."
STARTED=1
list=$(printf '%s,' "$@" | tr -s '[:space:]' ',')

wanted=()
IFS=',' read -ra parts <<<"$list"
for part in "${parts[@]}"; do
  name=$(tr -d '[:space:]`' <<<"$part")
  [ -n "$name" ] || continue
  [[ $name =~ $NAME_RE ]] || refuse "\`$(tr -d '`' <<<"$part" | head -c 60)\` is not the name of a component in the official registry."
  [ -e "$UI_DIR/$name.tsx" ] && { echo "$name is already in $UI_DIR."; continue; }
  wanted+=("$name")
done
[ "${#wanted[@]}" -le "$MAX" ] || refuse "more than $MAX components at once."
[ "${#wanted[@]}" -gt 0 ] || { echo "Every component named is already there."; exit 0; }

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
        "Add the package first with add-package.sh, if the admission rules allow it."
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
    "Add the package first with add-package.sh, if the admission rules allow it."
changed=$(git status --porcelain --untracked-files=all | cut -c4-)
outside=$(grep -vE "^$UI_DIR/[a-z0-9-]+\.tsx$" <<<"$changed" || true)
[ -z "$outside" ] || refuse "\`shadcn add\` changed files outside $UI_DIR: $(tr '\n' ' ' <<<"$outside")"
modified=$(git status --porcelain -- "$UI_DIR" | grep -v '^??' || true)
[ -z "$modified" ] || refuse "\`shadcn add\` changed components that were already there: $(cut -c4- <<<"$modified" | tr '\n' ' ')"
[ -n "$changed" ] || refuse "\`shadcn add ${wanted[*]}\` added no files."

pnpm --dir "$UI" exec prettier --write $(sed "s|^$UI/||" <<<"$changed") >/dev/null
git add -- $changed
git commit -q -m "Add the shadcn/ui components $(tr '\n' ' ' <<<"$changed" | sed -E "s|$UI_DIR/||g; s|\.tsx||g; s/ +$//; s/ /, /g")"
echo "Added and committed: $(tr '\n' ' ' <<<"$changed")"
