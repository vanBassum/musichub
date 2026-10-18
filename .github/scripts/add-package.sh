#!/usr/bin/env bash
# The only way autopilot's Claude adds a package. It runs in the sandbox (see
# sandbox-job.sh), from the repository root, and changes only the project file
# or musichub-ui/package.json and pnpm-lock.yaml. Claude commits the result.
#
#   bash /work/trusted/add-package.sh nuget <PackageId> <version>
#   bash /work/trusted/add-package.sh npm <name> <version> [--dev]
#
# Only an exact version of a package from the default registry (nuget.org, npm),
# by name: no version range, no tag such as `latest`, no URL, path, git or other
# registry. That keeps the change small enough for the api and ui reviewers to
# judge: which package, which version, why. Whether a package may be added at all
# is decided by the admission rules (.github/autopilot/admission.md, 2.6) and the
# understanding, not here. An existing package is not changed: an upgrade is
# refused. A refusal prints `REFUSED: <reason>` and exits 3, and changes nothing.
#
# Tested by .github/scripts/add-package.test.sh (with stub dotnet and pnpm).
set -euo pipefail

API_PROJECT=musichub-api/MusicHub.Api.csproj
UI=musichub-ui
VERSION_RE='^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$'
NUGET_RE='^[A-Za-z0-9_]+([.-][A-Za-z0-9_]+)*$'
NPM_RE='^(@[a-z0-9][a-z0-9._-]*/)?[a-z0-9][a-z0-9._-]*$'

refuse() { echo "REFUSED: $*"; exit 3; }
# undo: back to the clean tree the script started from.
undo() { git reset -q --hard; git clean -fdq; }

[ $# -ge 3 ] || refuse "usage: add-package.sh nuget <PackageId> <version> | npm <name> <version> [--dev]"
kind=$1 name=$2 version=$3 dev=${4:-}
[[ $version =~ $VERSION_RE ]] || refuse "\`$(head -c 60 <<<"$version")\` is not an exact version (like 1.2.3)."
[ $# -le 4 ] || refuse "too many arguments."
# A clean tree, so that what the package manager changed is all there is to check.
[ -z "$(git status --porcelain --untracked-files=all)" ] || refuse "commit your work first: add-package.sh needs a clean tree."

case $kind in
  nuget)
    [ -z "$dev" ] || refuse "--dev is only for npm."
    [ ${#name} -le 100 ] && [[ $name =~ $NUGET_RE ]] || refuse "\`$(head -c 60 <<<"$name")\` is not a NuGet package ID."
    if grep -qiE "<PackageReference[^>]+Include=\"$(sed 's/\./\\./g' <<<"$name")\"" "$API_PROJECT"; then
      refuse "$name is already a package of $API_PROJECT; autopilot doesn't change an existing package."
    fi
    dotnet add "$API_PROJECT" package "$name" --version "$version" \
      || { undo; refuse "\`dotnet add package $name --version $version\` failed."; }
    changed=$(git status --porcelain --untracked-files=all | cut -c4- | grep -vxF "$API_PROJECT" || true)
    [ -z "$changed" ] || { undo; refuse "adding $name changed more than $API_PROJECT: $(tr '\n' ' ' <<<"$changed")"; }
    echo "Added $name $version to $API_PROJECT. Commit it."
    ;;
  npm)
    case $dev in "" | --dev) ;; *) refuse "the fourth argument can only be --dev." ;; esac
    [ ${#name} -le 100 ] && [[ $name =~ $NPM_RE ]] || refuse "\`$(head -c 60 <<<"$name")\` is not an npm package name."
    if jq -e --arg n "$name" '((.dependencies // {}) + (.devDependencies // {})) | has($n)' "$UI/package.json" >/dev/null; then
      refuse "$name is already a package of $UI; autopilot doesn't change an existing package."
    fi
    pnpm --dir "$UI" add --save-exact ${dev:+--save-dev} "$name@$version" \
      || { undo; refuse "\`pnpm add $name@$version\` failed."; }
    changed=$(git status --porcelain --untracked-files=all | cut -c4- | grep -vxF -e "$UI/package.json" -e "$UI/pnpm-lock.yaml" || true)
    [ -z "$changed" ] || { undo; refuse "adding $name changed more than package.json and pnpm-lock.yaml: $(tr '\n' ' ' <<<"$changed")"; }
    echo "Added $name $version to $UI${dev:+ (dev)}. Commit package.json and pnpm-lock.yaml."
    ;;
  *) refuse "the kind is nuget or npm, not \`$(head -c 20 <<<"$kind")\`." ;;
esac
