#!/usr/bin/env bash
# Tests for issue-images.sh against a fake `gh` that serves canned issues and a fake
# `curl` that records every URL it is asked for. Needs bash and jq.
# Run: bash .github/scripts/issue-images.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
II="$HERE/issue-images.sh"
WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT

# --- fake gh and curl ---------------------------------------------------------
# `gh api [-H <h>] <endpoint> [--paginate] --jq <f>` prints jq <f> over
# $SCENARIO/<name>.json, <name> being the endpoint after repos/o/r/ with '/' turned
# into '_'; a missing file is a 404. Every endpoint must be asked for as rendered HTML.
# `curl ... -o <file> ... <url>` appends <url> to $SCENARIO/fetched and answers by the
# URL: one ending in .png, .jpg or .svg gets that type, one containing "fail" fails,
# one containing "big" is too large.
mkdir -p "$WORK/bin"
cat >"$WORK/bin/gh" <<'EOF'
#!/usr/bin/env bash
[ "$1" = api ] || exit 2
shift; endpoint=""; filter=.; html=0
while [ $# -gt 0 ]; do
  case $1 in
    -H) [ "$2" = "Accept: application/vnd.github.full+json" ] && html=1; shift 2 ;;
    --jq) filter=$2; shift 2 ;;
    --paginate) shift ;;
    *) endpoint=$1; shift ;;
  esac
done
[ "$html" = 1 ] || { echo "not asked for body_html: $endpoint" >&2; exit 1; }
name=${endpoint#repos/*/*/}; name=${name//\//_}
[ -e "$SCENARIO/$name.json" ] || { echo "HTTP 404: Not Found ($endpoint)" >&2; exit 1; }
jq -r "$filter" "$SCENARIO/$name.json"
EOF
cat >"$WORK/bin/curl" <<'EOF'
#!/usr/bin/env bash
out=""; url=""
while [ $# -gt 0 ]; do
  case $1 in
    -o) out=$2; shift 2 ;;
    --proto|--max-time|--max-filesize|-w) shift 2 ;;
    -*) shift ;;
    *) url=$1; shift ;;
  esac
done
echo "$url" >>"$SCENARIO/fetched"
case $url in
  *fail*) exit 22 ;;
  *big*) exit 63 ;;
  *.png\?*|*.png) printf 'PNG' >"$out"; printf 'image/png' ;;
  *.jpg\?*|*.jpg) printf 'JPG' >"$out"; printf 'image/jpeg' ;;
  *.svg\?*|*.svg) printf '<svg/>' >"$out"; printf 'image/svg+xml' ;;
  *) printf 'x' >"$out"; printf 'text/html' ;;
esac
EOF
chmod +x "$WORK/bin/gh" "$WORK/bin/curl"
export PATH="$WORK/bin:$PATH"

P=https://private-user-images.githubusercontent.com/1
# scenario <name> <body_html> [<comment body_html>...]: issue 12 with that body and
# those comments, the first by @vanBassum, the rest by @someone.
scenario() {
  SCENARIO="$WORK/$1"; export SCENARIO; OUT="$SCENARIO/images"
  mkdir -p "$SCENARIO"; : >"$SCENARIO/fetched"
  jq -n --arg h "$2" '{body_html: $h}' >"$SCENARIO/issues_12.json"
  shift 2
  local first=1 c
  echo '[]' >"$SCENARIO/issues_12_comments.json"
  for c in "$@"; do
    jq --arg h "$c" --arg u "$([ $first = 1 ] && echo vanBassum || echo someone)" \
      '. + [{body_html: $h, user: {login: $u}, created_at: "2026-10-17T12:00:00Z"}]' \
      "$SCENARIO/issues_12_comments.json" >"$SCENARIO/c.tmp" && mv "$SCENARIO/c.tmp" "$SCENARIO/issues_12_comments.json"
    first=0
  done
}
run() { bash "$II" o/r 12 "$OUT" >"$SCENARIO/output" 2>&1; echo $? >"$SCENARIO/exit"; }

PASS=0; FAILED=0
ok() { PASS=$((PASS + 1)); echo "ok    $1"; }
no() { FAILED=$((FAILED + 1)); echo "FAIL  $1"; sed 's/^/      /' "$SCENARIO/output" "$OUT/index.md" 2>/dev/null; }
# check <description> <condition...>
check() { local d=$1; shift; if "$@"; then ok "$d"; else no "$d"; fi; }
has() { grep -qF -- "$2" "$1"; }
lacks() { ! grep -qF -- "$2" "$1"; }
exits() { [ "$(cat "$SCENARIO/exit")" = "$1" ]; }
files() { [ "$(cd "$OUT" && ls | tr '\n' ' ')" = "$1" ]; }

# --- cases ----------------------------------------------------------------------
scenario attached \
  "<p>Like this:</p><p><a href=\"x\"><img width=\"10\" alt=\"Concept &amp; layout\" src=\"$P/a.png?jwt=1&amp;x=2\" style=\"max-width: 100%\"></a></p>" \
  "<p>Also</p><img
  alt=\"Second\" src=\"$P/b.jpg?jwt=2\">"
run
check "attached images are downloaded, in order" files "image-01.png image-02.jpg index.md "
check "the index says where each image is, with its alt text" \
  has "$OUT/index.md" '- `image-01.png`: in the issue body, alt text "Concept & layout"'
check "a comment's image names its author and date" \
  has "$OUT/index.md" '- `image-02.jpg`: in the comment by @vanBassum of 2026-10-17, alt text "Second"'
check "entities in the URL are decoded" has "$SCENARIO/fetched" "$P/a.png?jwt=1&x=2"
check "it succeeds" exits 0

scenario external \
  "<img src=\"https://camo.githubusercontent.com/abc/x.png\" alt=\"Ext\" data-canonical-src=\"https://example.com/x.png\">" \
  "<img alt=\"Plain http\" src=\"http://private-user-images.githubusercontent.com/1/c.png\">" \
  "<img alt=\"Lookalike\" src=\"https://private-user-images.githubusercontent.com.evil.example/c.png\">"
run
check "an external image, a plain http URL or a lookalike host is never fetched" [ ! -s "$SCENARIO/fetched" ]
check "but each is listed as not downloaded" \
  has "$OUT/index.md" '- in the issue body ("Ext"): not an image attached to GitHub, not downloaded'
check "with its comment" \
  has "$OUT/index.md" '- in the comment by @someone of 2026-10-17 ("Lookalike"): not an image attached to GitHub'
check "and the index says nothing was attached" has "$OUT/index.md" "have no images attached"

scenario rejected \
  "<img alt=\"Vector\" src=\"$P/v.svg?jwt=1\"><img alt=\"Gone\" src=\"$P/fail.png\"><img alt=\"Huge\" src=\"$P/big.png\"><img alt=\"Ok\" src=\"$P/ok.png\">"
run
check "an SVG, a failed and a too large download are left out, the rest is kept" files "image-01.png index.md "
check "the SVG is listed with its type" has "$OUT/index.md" '("Vector"): download failed or not a PNG, JPEG, GIF or WebP image (image/svg+xml)'
check "the failed download is listed" has "$OUT/index.md" '("Gone"): download failed'
check "a failed image doesn't fail the step" exits 0
check "no partial download is left behind" [ ! -e "$OUT/.download" ]

scenario limit "<img alt=\"One\" src=\"$P/1.png\"><img alt=\"Two\" src=\"$P/2.png\">"
MAX_IMAGES=1 run
check "at most MAX_IMAGES images are downloaded" files "image-01.png index.md "
check "the rest is listed" has "$OUT/index.md" '("Two"): more than 1 images, not downloaded'
check "and never fetched" lacks "$SCENARIO/fetched" "$P/2.png"

scenario none "<p>No images here.</p>"
run
check "an issue without images gets an index that says so" has "$OUT/index.md" "The issue and its comments have no images attached."

scenario unreadable ""
rm "$SCENARIO/issues_12.json"
run
check "an issue that can't be read fails the step" exits 1

echo "$PASS passed, $FAILED failed"
[ "$FAILED" -eq 0 ]
