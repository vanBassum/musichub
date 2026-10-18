#!/usr/bin/env bash
# Tests add-package.sh against stub dotnet and pnpm, in a scratch repository: what
# it adds, and every refusal (which must leave the tree as it was). Needs bash, git
# and jq.
# Run: bash .github/scripts/add-package.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
SCRIPT="$HERE/add-package.sh"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
pass=0 failed=0
ok() { echo "ok   $1"; pass=$((pass + 1)); }
no() { echo "FAIL $1"; failed=$((failed + 1)); sed 's/^/      /' "$WORK/out"; }

# The stubs: `dotnet add <project> package <id> --version <v>` adds a
# PackageReference; `pnpm --dir <ui> add --save-exact [--save-dev] <name>@<v>` adds
# the package to package.json and a line to the lockfile. $STUB_FAIL makes either
# fail, $STUB_EXTRA makes either also write that file. Every call is logged.
mkdir -p "$WORK/bin"
cat >"$WORK/bin/dotnet" <<'EOF'
#!/usr/bin/env bash
echo "dotnet $*" >>"$CALLS"
[ -z "${STUB_FAIL:-}" ] || exit 1
[ "$1 $3" = "add package" ] || exit 2
sed -i "s|</ItemGroup>|  <PackageReference Include=\"$4\" Version=\"$6\" />\n  </ItemGroup>|" "$2"
[ -z "${STUB_EXTRA:-}" ] || echo extra >"$STUB_EXTRA"
EOF
cat >"$WORK/bin/pnpm" <<'EOF'
#!/usr/bin/env bash
echo "pnpm $*" >>"$CALLS"
[ -z "${STUB_FAIL:-}" ] || exit 1
[ "$1 $3 $4" = "--dir add --save-exact" ] || exit 2
dir=$2; shift 4
key=dependencies; [ "$1" = --save-dev ] && { key=devDependencies; shift; }
name=${1%@*} version=${1##*@}
jq --arg k "$key" --arg n "$name" --arg v "$version" '.[$k][$n] = $v' "$dir/package.json" >"$dir/p.tmp" && mv "$dir/p.tmp" "$dir/package.json"
echo "$name@$version" >>"$dir/pnpm-lock.yaml"
[ -z "${STUB_EXTRA:-}" ] || echo extra >"$STUB_EXTRA"
EOF
chmod +x "$WORK/bin/dotnet" "$WORK/bin/pnpm"
export PATH="$WORK/bin:$PATH" CALLS="$WORK/calls"

# run <args>...: a fresh repository with the API project and the UI's package.json,
# then the script. Sets REPO and CODE.
run() {
  REPO="$WORK/repo-$RANDOM$RANDOM"
  mkdir -p "$REPO/musichub-api" "$REPO/musichub-ui"
  (
    cd "$REPO" || exit 1
    git init -q && git config core.autocrlf false && git config user.name t && git config user.email t@t.invalid
    printf '<Project>\n  <ItemGroup>\n    <PackageReference Include="AWSSDK.S3" Version="4.0.20" />\n  </ItemGroup>\n</Project>\n' >musichub-api/MusicHub.Api.csproj
    echo '{"dependencies":{"react":"19.0.0"},"devDependencies":{"vite":"7.0.0"}}' >musichub-ui/package.json
    echo "lock" >musichub-ui/pnpm-lock.yaml
    git add -A && git commit -q -m base
    [ -z "${DIRTY:-}" ] || echo wip >musichub-ui/src.ts
    : >"$CALLS"
    bash "$SCRIPT" "$@" >"$WORK/out" 2>&1
  )
  CODE=$?
}
clean() { [ -z "$(git -C "$REPO" status --porcelain --untracked-files=all)" ]; }
changed() { git -C "$REPO" status --porcelain --untracked-files=all | cut -c4- | sort | tr '\n' ' '; }
refused() { # refused <description> <reason pattern>
  if [ "$CODE" = 3 ] && grep -q "^REFUSED: .*$2" "$WORK/out" && { [ -n "${DIRTY:-}" ] || clean; }; then ok "$1"; else no "$1 (exit $CODE, changed: $(changed))"; fi
}

run nuget Npgsql.EntityFrameworkCore.PostgreSQL 10.0.0
if [ "$CODE" = 0 ] && [ "$(changed)" = "musichub-api/MusicHub.Api.csproj " ] \
  && grep -q 'Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0"' "$REPO/musichub-api/MusicHub.Api.csproj"; then
  ok "a NuGet package is added to the API project, at that version"; else no "a NuGet package is added to the API project, at that version"; fi
run npm zod 3.25.76
if [ "$CODE" = 0 ] && [ "$(changed)" = "musichub-ui/package.json musichub-ui/pnpm-lock.yaml " ] \
  && [ "$(jq -r .dependencies.zod "$REPO/musichub-ui/package.json")" = 3.25.76 ]; then
  ok "an npm package is added with pnpm, exact, lockfile included"; else no "an npm package is added with pnpm, exact, lockfile included"; fi
grep -q -- "pnpm --dir musichub-ui add --save-exact zod@3.25.76" "$CALLS" && ok "with --save-exact" || no "with --save-exact"
run npm @testing-library/react 16.3.0 --dev
[ "$CODE" = 0 ] && [ "$(jq -r '.devDependencies["@testing-library/react"]' "$REPO/musichub-ui/package.json")" = 16.3.0 ] \
  && ok "a scoped dev dependency" || no "a scoped dev dependency"
run nuget Some.Package 1.0.0-beta.2
[ "$CODE" = 0 ] && ok "a prerelease version is exact too" || no "a prerelease version is exact too"

run nuget AWSSDK.S3 4.0.21; refused "an existing NuGet package is not changed" "already a package"
run nuget awssdk.s3 4.0.21; refused "whatever its case" "already a package"
run npm react 19.1.0; refused "an existing npm package is not changed" "already a package"
run npm vite 7.1.0; refused "nor an existing dev dependency" "already a package"
for v in '^1.2.3' '~1.2.3' '1.2' 'latest' '>=1.0.0' '1.2.3 || 2' '*' '1.2.3; rm -rf /'; do
  run npm left-pad "$v"; refused "the version '$v' is refused" "not an exact version"
done
for n in 'https://evil.example/x.tgz' 'git+ssh://x' '../x' 'file:x' 'Left-Pad' 'a b' '@scope' 'x@1.0.0' '-x' '--registry=x'; do
  run npm "$n" 1.0.0; refused "the npm name '$n' is refused" "not an npm package name"
done
for n in 'https://evil' '../x' 'x y' 'x;y' '.x' 'x..y' '--source'; do
  run nuget "$n" 1.0.0; refused "the NuGet ID '$n' is refused" "not a NuGet package ID"
done
run pip requests 2.0.0; refused "an unknown kind is refused" "the kind is nuget or npm"
run nuget X 1.0.0 --dev; refused "--dev on NuGet is refused" "only for npm"
run npm x 1.0.0 --global; refused "another fourth argument is refused" "can only be --dev"
run npm x 1.0.0 --dev extra; refused "too many arguments are refused" "too many arguments"
run npm x; refused "too few arguments are refused" "usage"
DIRTY=1; run npm zod 3.25.76; refused "a dirty tree is refused" "clean tree"
grep -q "pnpm" "$CALLS" && no "and pnpm never runs" || ok "and pnpm never runs"
DIRTY=

STUB_FAIL=1 run nuget X 1.0.0; refused "a failing dotnet add is refused and undone" "failed"
STUB_FAIL=1 run npm x 1.0.0; refused "a failing pnpm add is refused and undone" "failed"
STUB_EXTRA=musichub-api/Program.cs run nuget X 1.0.0; refused "a NuGet add that changes another file is refused and undone" "changed more than"
STUB_EXTRA=musichub-ui/.npmrc run npm x 1.0.0; refused "an npm add that changes another file is refused and undone" "changed more than"

echo "$pass passed, $failed failed"
[ "$failed" = 0 ]
