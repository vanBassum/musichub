#!/usr/bin/env bash
# Pulls a main build onto the strato host, deploys it to the acceptance
# environment, and waits until it runs there and is healthy.
#
#   deploy-acceptance.sh <image:sha-tag> <commit sha>
#
# Runs on the self-hosted strato runner (build.yml, deploy-acceptance). The
# acceptance stack is stacks/musichub-acc in strato-stack: container
# musichub-acc, which runs ghcr.io/vanbassum/musichub:main and is updated only
# by its own Watchtower, listening on the internal deploy-network that the
# runners join. Watchtower has no registry credentials and runs with no-pull:
# this job pulls the immutable sha tag (logged in to GHCR by the workflow),
# points the local :main at it, and Watchtower recreates the container from it.
#
# Env: WATCHTOWER_TOKEN (required), and for tests CONTAINER, WATCHTOWER_URL,
# PUBLIC_URL, HEALTH_TIMEOUT (seconds), PULL_FROM (a local image to tag as
# <image:sha-tag> instead of pulling it).
set -euo pipefail

IMAGE="${1:?usage: deploy-acceptance.sh <image:sha-tag> <commit sha>}"
SHA="${2:?usage: deploy-acceptance.sh <image:sha-tag> <commit sha>}"
: "${WATCHTOWER_TOKEN:?WATCHTOWER_TOKEN is not set (secret ACCEPTANCE_DEPLOY_TOKEN)}"
CONTAINER="${CONTAINER:-musichub-acc}"
WATCHTOWER_URL="${WATCHTOWER_URL:-http://musichub-acc-watchtower:8080}"
PUBLIC_URL="${PUBLIC_URL:-https://musichub-acc.vanbassum.com/}"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-300}"
PULL_FROM="${PULL_FROM:-}"
# Keeps the running image inspectable while its tag moves on. The host uses
# the containerd image store, where an image that loses its last tag can no
# longer be inspected, even while a container runs it; Watchtower then has no
# image info and skips the container instead of updating it.
PIN="$CONTAINER:running"

fail() {
  echo "::error::$1"
  if docker container inspect "$CONTAINER" >/dev/null 2>&1; then
    echo "--- last log lines of $CONTAINER ---"
    docker logs --tail 80 "$CONTAINER" 2>&1 || true
  fi
  exit 1
}

# Watchtower only recreates an existing container. On a fresh host, the stack
# has to be started once by hand (see stacks/musichub-acc), and it never pulls,
# so this job puts the image there first and then stops.
if ! docker container inspect "$CONTAINER" >/dev/null 2>&1; then
  if [ -n "$PULL_FROM" ]; then docker tag "$PULL_FROM" "$IMAGE"; else docker pull "$IMAGE"; fi
  docker tag "$IMAGE" "${IMAGE%:*}:main"
  fail "Container $CONTAINER does not exist. ${IMAGE%:*}:main is on the host now: start the acceptance stack there (make up stack=musichub-acc), then re-run this job"
fi
# The tag the container was created with (ghcr.io/vanbassum/musichub:main):
# Watchtower recreates from whatever that tag points at locally.
TARGET=$(docker inspect "$CONTAINER" --format '{{.Config.Image}}')

# 1. Pin what runs now, then pull this commit's image. Its sha tag is never
#    overwritten, so it is exactly this commit's build.
docker tag "$(docker inspect "$CONTAINER" --format '{{.Image}}')" "$PIN" ||
  echo "::warning::Could not pin the image $CONTAINER runs; Watchtower may skip it"
if [ -n "$PULL_FROM" ]; then docker tag "$PULL_FROM" "$IMAGE"; else docker pull "$IMAGE"; fi
revision=$(docker image inspect "$IMAGE" --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')
[ "$revision" = "$SHA" ] ||
  fail "$IMAGE on the host is commit '$revision', not $SHA"
image_id=$(docker image inspect "$IMAGE" --format '{{.Id}}')
docker tag "$IMAGE" "$TARGET"
echo "Deploying $IMAGE ($image_id) as $TARGET, commit $SHA, to $CONTAINER"

# 2. Ask Watchtower to recreate it. A targeted update waits for any update in
#    progress and answers when done, with a summary.
response=$(curl -sS --max-time 660 -w '\n%{http_code}' -X POST \
  -H "Authorization: Bearer $WATCHTOWER_TOKEN" \
  "$WATCHTOWER_URL/v1/update?container=$CONTAINER") ||
  fail "Could not reach Watchtower at $WATCHTOWER_URL (is this runner on deploy-network?)"
status=$(tail -n1 <<<"$response")
body=$(sed '$d' <<<"$response")
echo "Watchtower answered $status: $body"
[ "$status" = 200 ] || fail "Watchtower update failed with HTTP $status"
failed=$(jq -r '.summary.failed // 0' <<<"$body")
[ "$failed" = 0 ] || fail "Watchtower reports $failed failed update(s)"
updated=$(jq -r '.summary.updated // 0' <<<"$body")
if [ "$updated" = 0 ] && [ "$(docker inspect "$CONTAINER" --format '{{.Image}}')" != "$image_id" ]; then
  fail "Watchtower updated nothing, and $CONTAINER still runs another image (see: docker logs $CONTAINER-watchtower)"
fi

# 3. Wait until the container runs exactly this image and its healthcheck
#    passes (it GETs / , the UI served by the API).
deadline=$((SECONDS + HEALTH_TIMEOUT))
while :; do
  running=$(docker inspect "$CONTAINER" --format '{{.Image}}' 2>/dev/null || true)
  health=$(docker inspect "$CONTAINER" --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' 2>/dev/null || true)
  if [ "$running" = "$image_id" ] && [ "$health" = healthy ]; then
    break
  fi
  [ "$health" = unhealthy ] && [ "$running" = "$image_id" ] &&
    fail "$CONTAINER runs the new image but is unhealthy"
  [ "$SECONDS" -lt "$deadline" ] ||
    fail "$CONTAINER not healthy on the new image after ${HEALTH_TIMEOUT}s (image ${running:-none}, health ${health:-none})"
  sleep 5
done
echo "$CONTAINER runs the new image and is healthy"
# Only now: after a failure the pin keeps the old image for a look or rollback.
docker rmi "$PIN" >/dev/null 2>&1 || true

# 4. And it is reachable from outside, through Traefik.
for _ in $(seq 12); do
  code=$(curl -s -o /dev/null --max-time 10 -w '%{http_code}' "$PUBLIC_URL" || true)
  if [ "$code" = 200 ]; then
    echo "$PUBLIC_URL answers 200"
    echo "### Acceptance updated to \`${SHA:0:12}\`: $PUBLIC_URL" >>"${GITHUB_STEP_SUMMARY:-/dev/null}"
    exit 0
  fi
  sleep 5
done
fail "$PUBLIC_URL answers $code instead of 200"
