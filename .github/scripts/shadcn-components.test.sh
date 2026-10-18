#!/usr/bin/env bash
# Tests shadcn-components.sh against a stub pnpm, in a scratch repository: what it
# adds and commits, and every refusal. Needs bash, git and jq.
# Run: bash .github/scripts/shadcn-components.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
SCRIPT="$HERE/shadcn-components.sh"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
pass=0 failed=0
ok() { echo "ok   $1"; pass=$((pass + 1)); }
no() { echo "FAIL $1"; failed=$((failed + 1)); }

# The stub: `shadcn view <name>` prints $REGISTRY/<name>.json or fails; `shadcn add`
# writes src/components/ui/<name>.tsx for each name, plus whatever $ADD_EXTRA names
# (a path relative to musichub-ui, or package.json to change it); prettier does
# nothing. Every call is logged.
mkdir -p "$WORK/bin" "$WORK/registry"
cat >"$WORK/bin/pnpm" <<'EOF'
#!/usr/bin/env bash
echo "$*" >>"$CALLS"
[ "$1" = --dir ] || exit 2
dir=$2; shift 2
[ "$1" = exec ] || exit 2
shift
case "$1 $2" in
  "shadcn view") cat "$REGISTRY/$3.json" 2>/dev/null ;;
  "shadcn add")
    shift 2
    for n in "$@"; do [ "$n" = --yes ] || echo "export {}" >"$dir/src/components/ui/$n.tsx"; done
    if [ -n "${ADD_EXTRA:-}" ]; then mkdir -p "$(dirname "$dir/$ADD_EXTRA")"; echo "// extra" >>"$dir/$ADD_EXTRA"; fi ;;
  prettier*) exit 0 ;;
  *) exit 2 ;;
esac
EOF
chmod +x "$WORK/bin/pnpm"
export PATH="$WORK/bin:$PATH" REGISTRY="$WORK/registry" CALLS="$WORK/calls"

item() { # item <name> <dependencies json> <registryDependencies json>
  printf '[{"name":"%s","dependencies":%s,"registryDependencies":%s,"type":"registry:ui"}]\n' "$1" "$2" "$3" >"$REGISTRY/$1.json"
}
item table '[]' '[]'
item checkbox '["cn@^0.5.0", "lucide-react"]' '[]'
item slider '["cn"]' '[]'
item chart '["recharts"]' '["card"]'
item card '[]' '[]'
item sidebar '[]' '["sheet"]'
item sheet '["@base-ui/react"]' '[]'
item evil '[]' '["https://example.com/r/x.json"]'

# run <args>...: a fresh repository with button.tsx, then the script with those
# arguments. Sets REPO and CODE.
run() {
  REPO="$WORK/repo-$RANDOM$RANDOM"
  mkdir -p "$REPO/musichub-ui/src/components/ui"
  (
    cd "$REPO" || exit 1
    git init -q && git config core.autocrlf false && git config user.name t && git config user.email t@t.invalid
    echo '{"dependencies":{"cn":"^0.5.0","lucide-react":"^1","@base-ui/react":"^1"},"devDependencies":{"prettier":"^3"}}' >musichub-ui/package.json
    echo "lock" >musichub-ui/pnpm-lock.yaml
    echo "export {}" >musichub-ui/src/components/ui/button.tsx
    git add -A && git commit -q -m base
    [ -z "${DIRTY:-}" ] || echo wip >musichub-ui/src/wip.ts
    : >"$CALLS"
    bash "$SCRIPT" "$@" >"$WORK/out" 2>&1
  )
  CODE=$?
}
commits() { git -C "$REPO" rev-list --count HEAD; }
clean() { [ -z "$(git -C "$REPO" status --porcelain --untracked-files=all -- musichub-ui)" ]; }
expect_blocked() { # expect_blocked <description> <reason pattern>
  if [ "$CODE" = 3 ] && grep -q "^REFUSED: .*$2" "$WORK/out" && { [ -n "${DIRTY:-}" ] || clean; } && [ "$(commits)" = 1 ]; then ok "$1"; else no "$1"; { cat "$WORK/out"; git -C "$REPO" status --short; } 2>/dev/null | sed 's/^/      /'; fi
}
added() { # added <description> <files, space separated>
  local files
  files=$(git -C "$REPO" show --name-only --format= HEAD | sed 's|musichub-ui/src/components/ui/||' | sort | tr '\n' ' ')
  if [ "$CODE" = 0 ] && clean && [ "$(commits)" = 2 ] && [ "$files" = "$2 " ]; then ok "$1"; else no "$1 (got: $files)"; fi
}
nothing() {
  if [ "$CODE" = 0 ] && clean && [ "$(commits)" = 1 ]; then ok "$1"; else no "$1"; fi
}

run; expect_blocked "no name is refused" "name at least one component"
run button; nothing "a component that is already there is skipped"

run table checkbox
added "two components are added and committed in one commit" "checkbox.tsx table.tsx"
grep -q "shadcn add table checkbox --yes" "$CALLS" && ok "and added in one shadcn add" || no "and added in one shadcn add"
run 'table, checkbox'; added "a comma separated list works too" "checkbox.tsx table.tsx"
run '`slider`,' button; added "backticks and an existing component are tolerated" "slider.tsx"
run sidebar; added "a pulled-in component whose packages are there is fine" "sidebar.tsx"
grep -q "shadcn view sheet" "$CALLS" && ok "and the pulled-in component is checked too" || no "and the pulled-in component is checked too"

run chart
expect_blocked "a component that needs a new npm package is refused" "npm package \`recharts\`"
grep -q "add-package.sh" "$WORK/out" && ok "and points to add-package.sh" || no "and points to add-package.sh"
grep -q "shadcn add" "$CALLS" && no "and shadcn add never runs" || ok "and shadcn add never runs"
run https://example.com/r/x.json; expect_blocked "a URL is refused" "not the name of a component"
run @acme/table; expect_blocked "another registry is refused" "not the name of a component"
run ../../evil; expect_blocked "a path is refused" "not the name of a component"
run 'table; rm -rf /'; expect_blocked "shell syntax is refused" "not the name of a component"
run evil; expect_blocked "a pulled-in URL is refused" "not a component of the official registry"
run nosuch; expect_blocked "a component the registry doesn't have is refused" "registry has no component"
run a1 a2 a3 a4 a5 a6 a7 a8 a9 a10 a11; expect_blocked "more than ten components are refused" "more than 10"
DIRTY=1; run table; expect_blocked "a dirty tree is refused, and left as it is" "clean tree"
[ -e "$REPO/musichub-ui/src/wip.ts" ] && ok "the work in progress is kept" || no "the work in progress is kept"
DIRTY=

ADD_EXTRA=package.json run table
expect_blocked "an add that changes package.json is refused and undone" "changed package.json"
ADD_EXTRA=src/lib/extra.ts run table
expect_blocked "an add that writes outside components/ui is refused and undone" "outside"
ADD_EXTRA=src/components/ui/button.tsx run table
expect_blocked "an add that changes an existing component is refused and undone" "already there"

echo "$pass passed, $failed failed"
[ "$failed" = 0 ]
