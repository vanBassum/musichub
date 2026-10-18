#!/usr/bin/env bash
# What runs inside the sandbox container (see sandbox.sh), in /work/repo. The job's
# trusted steps copy this script from main to /work/trusted, so nothing in the
# repository under test can change it.
#
#   sandbox-job.sh ci      the PR checks: restore, build and test the API, lint and
#                          build the UI. Fails on the first step that fails.
set -euo pipefail

step() { echo "::group::$*"; }
end() { echo "::endgroup::"; }

cmd_ci() {
  step "Restore"
  dotnet restore MusicHub.slnx
  pnpm --dir musichub-ui install --frozen-lockfile
  end
  step "Build the API"; dotnet build MusicHub.slnx --configuration Release --no-restore; end
  step "Test the API"; dotnet test MusicHub.slnx --configuration Release --no-build --no-restore; end
  step "Lint the UI"; pnpm --dir musichub-ui lint; end
  step "Build the UI"; pnpm --dir musichub-ui build; end
}

case "${1:-}" in
  ci) cmd_ci ;;
  *) echo "Usage: sandbox-job.sh ci" >&2; exit 2 ;;
esac
