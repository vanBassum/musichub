#!/usr/bin/env bash
# Tests sandbox.sh against a stub docker that records every call: how the container
# is started (isolation flags, mounts, the environment it gets) and that it is
# removed. The image itself is tried by the job that uses it. Needs bash and git.
# Run: bash .github/scripts/sandbox.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
SCRIPT="$HERE/sandbox.sh"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
pass=0 failed=0
ok() { echo "ok   $1"; pass=$((pass + 1)); }
no() { echo "FAIL $1"; failed=$((failed + 1)); sed 's/^/      /' "$WORK/out" "$CALLS" 2>/dev/null; }

mkdir -p "$WORK/bin"
cat >"$WORK/bin/docker" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$*" >>"$CALLS"
case "$1 $2" in
  "image inspect") [ -z "${NO_IMAGE:-}" ]; exit ;;
  "network inspect") exit 0 ;;
  "run --rm")
    case " $* " in
      *" --entrypoint id "*) echo 1654 ;;
      *" --entrypoint chown "*) exit 0 ;;
      *) env >"$ENV_SEEN"; exit "${RUN_EXIT:-0}" ;;
    esac ;;
esac
exit 0
EOF
chmod +x "$WORK/bin/docker"
export PATH="$WORK/bin:$PATH" CALLS="$WORK/calls" ENV_SEEN="$WORK/env"

G="git -c user.name=t -c user.email=t@example.com -c init.defaultBranch=main -c commit.gpgsign=false"
$G init -q "$WORK/checkout" && echo a >"$WORK/checkout/a" && $G -C "$WORK/checkout" add -A && $G -C "$WORK/checkout" commit -qm one
DIR="$WORK/sb"

# --- prepare ------------------------------------------------------------------
(cd "$WORK/checkout" && bash "$SCRIPT" prepare "$DIR" .) >"$WORK/out" 2>&1
[ "$(git -C "$DIR/repo" rev-parse HEAD)" = "$(git -C "$WORK/checkout" rev-parse HEAD)" ] \
  && ok "prepare copies the checked-out commit (also from a relative path)" || no "prepare copies the checked-out commit"
[ -d "$DIR/in" ] && [ -d "$DIR/out" ] && [ -d "$DIR/home" ] && [ -d "$DIR/trusted" ] && ok "and makes in, out, home and trusted" || no "and makes in, out, home and trusted"
git -C "$DIR/repo" config --local --get-regexp 'remote|extraheader|credential' >/dev/null && no "the copy has no remote or credentials" || ok "the copy has no remote or credentials"

# actions/checkout fetches one commit by default: a shallow checkout.
echo b >"$WORK/checkout/b" && $G -C "$WORK/checkout" add -A && $G -C "$WORK/checkout" commit -qm two
git clone -q --depth 1 "file://$WORK/checkout" "$WORK/shallow"
bash "$SCRIPT" prepare "$WORK/sb-shallow" "$WORK/shallow" >"$WORK/out" 2>&1 \
  && [ "$(git -C "$WORK/sb-shallow/repo" rev-parse HEAD)" = "$(git -C "$WORK/checkout" rev-parse HEAD)" ] \
  && ok "prepare copies a shallow checkout too" || no "prepare copies a shallow checkout too"

# --- run ----------------------------------------------------------------------
: >"$CALLS"
CLAUDE_CODE_OAUTH_TOKEN=claude-secret SANDBOX_ISSUE=12 \
  GH_TOKEN=gh-secret GITHUB_TOKEN=gh-secret2 GITHUB_RUN_ID=77 GITHUB_JOB=build \
  bash "$SCRIPT" run "$DIR" 30 bash /work/trusted/sandbox-job.sh ci >"$WORK/out" 2>&1
code=$?
[ "$code" = 0 ] && ok "run passes the container's result on" || no "run passes the container's result on (exit $code)"
main=$(grep -E '^run --rm --name sandbox-77-1-build ' "$CALLS")
[ -n "$main" ] && ok "the container is named after the run and job" || no "the container is named after the run and job"
for flag in "--cap-drop ALL" "--security-opt no-new-privileges" "--read-only" "--network musichub-sandbox" "--pids-limit" "--memory" "--cpus" \
            "-v $DIR:/work " "-v $DIR/in:/work/in:ro" "-v $DIR/trusted:/work/trusted:ro" "-w /work/repo" "-e HOME=/work/home" \
            "bash /work/trusted/sandbox-job.sh ci"; do
  grep -qF -- "$flag" <<<"$main" && ok "the container runs with '$flag'" || no "the container runs with '$flag'"
done
for bad in "docker.sock" "--privileged" "--user 0" "--network host" "-v /:" "$HOME:"; do
  grep -qF -- "$bad" <<<"$main" && no "the container never gets '$bad'" || ok "the container never gets '$bad'"
done
grep -qF -- "-e SANDBOX_ISSUE" <<<"$main" && ok "it gets the SANDBOX_ parameters" || no "it gets the SANDBOX_ parameters"
for v in CLAUDE_CODE_OAUTH_TOKEN GH_TOKEN GITHUB_TOKEN; do
  grep -qF -- "-e $v" <<<"$main" && no "but never $v" || ok "but never $v"
done
grep -qE -- '-e [A-Z_]+=' <<<"$main" | grep -v HOME && no "no value is put on the command line" || ok "no value is put on the command line"
grep -qE '^run --rm --user 0 --network none -v .* --entrypoint chown .* -R 1654:1654 /work$' "$CALLS" \
  && ok "the directory is handed to the sandbox user first" || no "the directory is handed to the sandbox user first"
grep -qE '^rm -f sandbox-77-1-build$' "$CALLS" && ok "the container is removed afterwards" || no "the container is removed afterwards"

: >"$CALLS"
RUN_EXIT=4 GITHUB_RUN_ID=77 GITHUB_JOB=build bash "$SCRIPT" run "$DIR" 30 true >"$WORK/out" 2>&1
code=$?
[ "$code" = 4 ] && ok "a failing container fails the run" || no "a failing container fails the run (exit $code)"
grep -qE '^rm -f sandbox-77-1-build$' "$CALLS" && ok "and is removed too" || no "and is removed too"

: >"$CALLS"
NO_IMAGE=1 bash "$SCRIPT" run "$DIR" 30 true >"$WORK/out" 2>&1
grep -qE '^build -q -t musichub-sandbox:[0-9a-f]{12} .*sandbox$' "$CALLS" && ok "a missing image is built from .github/sandbox, tagged by its Dockerfile" || no "a missing image is built from .github/sandbox"

bash "$SCRIPT" run "$WORK/nowhere" 30 true >"$WORK/out" 2>&1 && no "an unprepared directory is refused" || ok "an unprepared directory is refused"
bash "$SCRIPT" run "$DIR" soon true >"$WORK/out" 2>&1 && no "a bad timeout is refused" || ok "a bad timeout is refused"

# --- clean --------------------------------------------------------------------
: >"$CALLS"
GITHUB_RUN_ID=77 GITHUB_JOB=build bash "$SCRIPT" clean "$DIR" >"$WORK/out" 2>&1
[ ! -e "$DIR" ] && ok "clean removes the directory" || no "clean removes the directory"
grep -qE '^rm -f sandbox-77-1-build$' "$CALLS" && ok "and the container" || no "and the container"
bash "$SCRIPT" clean "$DIR" >"$WORK/out" 2>&1 && ok "cleaning twice is fine" || no "cleaning twice is fine"

echo "$pass passed, $failed failed"
[ "$failed" = 0 ]
