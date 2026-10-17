#!/usr/bin/env bash
# Downloads the images attached to an issue and its comments, so that a Claude step
# can look at them with its Read tool. Claude itself gets no network access for it.
#
#   issue-images.sh <owner/repo> <issue> <dir>
#
# Needs GH_TOKEN (read access to the issue; the workflow's GITHUB_TOKEN will do),
# gh, jq and curl. In a private repository an attachment URL in the markdown
# (github.com/user-attachments/assets/...) needs the uploader's session, but the
# rendered HTML of the API carries a signed private-user-images URL for each image,
# valid for a few minutes and fetchable without a token. Those are downloaded, right
# after reading them.
#
# Writes <dir>/image-NN.<ext> and <dir>/index.md, which says for each image where it
# is (the issue body or which comment) with its alt text, and lists the images that
# were not downloaded, and why. Only GitHub's own attachment hosts are fetched, over
# https, without following redirects, at most MAX_IMAGES images of at most MAX_BYTES
# each, and only PNG, JPEG, GIF and WebP: an external image is listed, not fetched,
# so an issue can't make the runner request an arbitrary URL. Exits non-zero only when
# the issue can't be read; a failed image is listed in the index instead.
set -uo pipefail

REPO="${1:?usage: issue-images.sh <owner/repo> <issue> <dir>}"
ISSUE="${2:?usage: issue-images.sh <owner/repo> <issue> <dir>}"
DIR="${3:?usage: issue-images.sh <owner/repo> <issue> <dir>}"
MAX_IMAGES="${MAX_IMAGES:-20}"
MAX_BYTES="${MAX_BYTES:-10485760}"
HTML='Accept: application/vnd.github.full+json'

for tool in gh jq curl; do
  command -v "$tool" >/dev/null || { echo "issue-images.sh needs $tool" >&2; exit 1; }
done
mkdir -p "$DIR"
sources="$DIR/.sources"
# One JSON object per line: where the HTML comes from, and the HTML.
gh api -H "$HTML" "repos/$REPO/issues/$ISSUE" \
  --jq '{where: "the issue body", html: (.body_html // "")} | @json' >"$sources" || exit 1
gh api -H "$HTML" "repos/$REPO/issues/$ISSUE/comments" --paginate \
  --jq '.[] | {where: "the comment by @\(.user.login) of \(.created_at[:10])", html: (.body_html // "")} | @json' >>"$sources" || exit 1

decode() { sed -e 's/&quot;/"/g' -e "s/&#39;/'/g" -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/&amp;/\&/g'; }
# attr <name> <tag>: the value of the attribute, entities decoded; empty if absent.
attr() { sed -nE 's/.*[[:space:]]'"$1"'="([^"]*)".*/\1/p' <<<"$2" | decode; }

n=0; fetched=""; skipped=""
while IFS= read -r line; do
  where=$(jq -r .where <<<"$line")
  while IFS= read -r tag; do
    [ -n "$tag" ] || continue
    src=$(attr src "$tag"); alt=$(attr alt "$tag" | tr -d '\r\n')
    case $src in
      https://private-user-images.githubusercontent.com/* | https://user-images.githubusercontent.com/*) ;;
      *) skipped+="- in $where (\"$alt\"): not an image attached to GitHub, not downloaded"$'\n'; continue ;;
    esac
    if [ "$n" -ge "$MAX_IMAGES" ]; then
      skipped+="- in $where (\"$alt\"): more than $MAX_IMAGES images, not downloaded"$'\n'; continue
    fi
    part="$DIR/.download"; rm -f "$part"
    type=$(curl --proto =https --fail --silent --max-time 60 --max-filesize "$MAX_BYTES" \
      -o "$part" -w '%{content_type}' "$src") || type="failed"
    case ${type%%;*} in
      image/png) ext=png ;; image/jpeg) ext=jpg ;; image/gif) ext=gif ;; image/webp) ext=webp ;;
      *) rm -f "$part"
         skipped+="- in $where (\"$alt\"): download failed or not a PNG, JPEG, GIF or WebP image (${type:-no type})"$'\n'
         continue ;;
    esac
    n=$((n + 1)); name=$(printf 'image-%02d.%s' "$n" "$ext")
    mv "$part" "$DIR/$name"
    fetched+="- \`$name\`: in $where, alt text \"$alt\""$'\n'
  done < <(jq -r .html <<<"$line" | tr '\r\n' '  ' | grep -oE '<img[^>]*>')
done <"$sources"
rm -f "$sources"

{
  echo "# Images of issue #$ISSUE"
  echo
  if [ -n "$fetched" ]; then printf '%s' "$fetched"; else echo "The issue and its comments have no images attached."; fi
  if [ -n "$skipped" ]; then
    echo
    echo "Not downloaded:"
    echo
    printf '%s' "$skipped"
  fi
} >"$DIR/index.md"
echo "$n image(s) downloaded to $DIR"
cat "$DIR/index.md"
