#!/usr/bin/env bash
# Runs a PR's own restore, build and tests in a throwaway container, away from the
# runner, which holds the Docker socket. A job's trusted steps prepare a directory,
# the container works in it, and it is removed afterwards.
#
#   sandbox.sh prepare <dir> <checkout>   a fresh copy of the commit checked out
#                                         in <checkout> (and its origin/main, if
#                                         fetched) at <dir>/repo, plus the empty
#                                         <dir>/in, out, home and trusted
#   sandbox.sh run <dir> <minutes> <command>...
#                                         run <command> in a new container from
#                                         .github/sandbox/Dockerfile, in
#                                         <dir>/repo; it is removed afterwards,
#                                         and killed after <minutes>
#   sandbox.sh clean <dir>                remove the container and <dir>
#
# Inside the container, <dir> is /work. /work/in and /work/trusted are read-only:
# the job's inputs, and the scripts from main. /work/out is where the run leaves
# its results, and /work/home is its home directory (NuGet and pnpm caches).
# Nothing else of the host is mounted.
#
# The container gets only HOME and the variables whose name starts with SANDBOX_
# (the job's parameters). It runs as the image's unprivileged user, with all
# capabilities dropped, no new privileges, a read-only root filesystem, memory, CPU
# and process limits. It is on its own Docker network, `musichub-sandbox`, which
# reaches the internet (NuGet, npm) but no stack on the host.
#
# The bind mounts are resolved by the host's Docker daemon. The strato runners
# mount their work directory at the same path on both sides
# (strato-stack, stacks/github-runner), so the runner's paths work as they are.
set -euo pipefail

fail() { echo "::error::$*" >&2; exit 1; }

HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
CONTEXT="$HERE/../sandbox"
NETWORK=musichub-sandbox

# image: the sandbox image for this Dockerfile, built once per host and version.
image() {
  local tag
  tag="musichub-sandbox:$(sha256sum "$CONTEXT/Dockerfile" | cut -c1-12)"
  if ! docker image inspect "$tag" >/dev/null 2>&1; then
    docker build -q -t "$tag" "$CONTEXT" >/dev/null || fail "Could not build the sandbox image."
  fi
  echo "$tag"
}

# as_root <dir> <command>...: a root container over <dir>, to hand it to the
# sandbox user or back to the runner. The runner may not be root, and the files the
# sandbox wrote belong to the sandbox user.
as_root() {
  local dir=$1; shift
  docker run --rm --user 0 --network none -v "$dir:/work" --entrypoint "$1" "$IMAGE" "${@:2}"
}

container_name() { echo "sandbox-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-1}-${GITHUB_JOB:-job}"; }

cmd_prepare() {
  local dir=$1 checkout=$2
  [ -d "$checkout/.git" ] || fail "$checkout is not a git checkout."
  checkout=$(cd "$checkout" && pwd)
  rm -rf "$dir"
  mkdir -p "$dir/repo" "$dir/in" "$dir/out" "$dir/home" "$dir/trusted"
  # A new repository that only fetches from the trusted checkout: its .git holds no
  # credentials and nothing of the runner.
  git init -q "$dir/repo"
  git -C "$dir/repo" fetch -q --no-tags --update-shallow "$checkout" "+HEAD:refs/heads/base"
  git -C "$dir/repo" fetch -q --no-tags --update-shallow "$checkout" "+refs/remotes/origin/main:refs/remotes/origin/main" 2>/dev/null || true
  git -C "$dir/repo" checkout -q base
}

cmd_run() {
  local dir=$1 minutes=$2 uid env=() v
  shift 2
  [ -d "$dir/repo" ] || fail "$dir was not prepared (sandbox.sh prepare)."
  [[ $minutes =~ ^[1-9][0-9]*$ ]] || fail "Minutes is not a number: '$minutes'."
  [ $# -gt 0 ] || fail "No command to run."
  IMAGE=$(image)
  docker network inspect "$NETWORK" >/dev/null 2>&1 || docker network create "$NETWORK" >/dev/null 2>&1 \
    || docker network inspect "$NETWORK" >/dev/null 2>&1 || fail "Could not create the $NETWORK network."
  uid=$(docker run --rm --network none --entrypoint id "$IMAGE" -u)
  as_root "$dir" chown -R "$uid:$uid" /work
  for v in $(compgen -e | grep -E '^SANDBOX_[A-Z0-9_]+$' || true); do
    [ -n "${!v:-}" ] && env+=(-e "$v")
  done
  NAME=$(container_name)
  docker rm -f "$NAME" >/dev/null 2>&1 || true
  # The container goes when this script does, also on a timeout or a cancelled job.
  trap 'docker rm -f "$NAME" >/dev/null 2>&1 || true' EXIT
  timeout -k 30 "${minutes}m" docker run --rm --name "$NAME" --init \
    --network "$NETWORK" --cap-drop ALL --security-opt no-new-privileges \
    --pids-limit 4096 --memory 6g --cpus 3 \
    --read-only --tmpfs /tmp:exec,size=2g \
    -v "$dir:/work" -v "$dir/in:/work/in:ro" -v "$dir/trusted:/work/trusted:ro" \
    -w /work/repo -e HOME=/work/home "${env[@]}" \
    "$IMAGE" "$@"
}

cmd_clean() {
  local dir=$1
  docker rm -f "$(container_name)" >/dev/null 2>&1 || true
  [ -d "$dir" ] || return 0
  if IMAGE=$(image 2>/dev/null); then
    as_root "$dir" chown -R "$(id -u):$(id -g)" /work >/dev/null 2>&1 || true
  fi
  rm -rf "$dir" || echo "::warning::Could not remove $dir."
}

case "${1:-}" in
  prepare) [ $# -eq 3 ] || fail "Usage: sandbox.sh prepare <dir> <checkout>"; cmd_prepare "$2" "$3" ;;
  run) [ $# -ge 4 ] || fail "Usage: sandbox.sh run <dir> <minutes> <command>..."; cmd_run "$2" "$3" "${@:4}" ;;
  clean) [ $# -eq 2 ] || fail "Usage: sandbox.sh clean <dir>"; cmd_clean "$2" ;;
  *) fail "Usage: sandbox.sh prepare <dir> <checkout> | run <dir> <minutes> <command>... | clean <dir>" ;;
esac
