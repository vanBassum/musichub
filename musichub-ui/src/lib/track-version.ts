// Presentation only: which tracks of an artist's overview are the ordinary studio
// versions. Nothing here changes identity, import or the catalog; a hidden track is
// one toggle away.
export type VersionKind = "regular" | "alternative" | "ambiguous"

// Words that name a different performance or mix of a song. Matched as whole words in
// the version text only, so a title such as "Live and Let Die" is never hidden.
const alternativeMarker =
  /\b(live|remix(?:ed)?|rmx|demo|acoustic|instrumental|karaoke|rehearsal|rework|re-?edit|bootleg|session)\b/i

// The parts of a title that qualify it: "(…)", "[…]" and a " - …" suffix.
function titleQualifiers(title: string) {
  const parts = [...title.matchAll(/[([]([^)\]]*)[)\]]/g)].map((m) => m[1])
  const dash = title.indexOf(" - ")
  if (dash >= 0) parts.push(title.slice(dash + 3))
  return parts
}

// `version` is what the source says about the version (a catalog track's version, a
// provider's disambiguation). A track nothing qualifies is regular; one with a
// qualifier that is no known alternative (a remaster, a radio edit) is ambiguous and
// stays in the default list rather than being guessed away.
export function classifyVersion(
  title: string,
  version: string | null | undefined
): VersionKind {
  const qualifiers = [...titleQualifiers(title), version ?? ""].filter(
    (q) => q.trim() !== ""
  )
  if (qualifiers.length === 0) return "regular"
  return qualifiers.some((q) => alternativeMarker.test(q))
    ? "alternative"
    : "ambiguous"
}

interface Listed {
  title: string
  detail: string
  key: string
  versionKind: VersionKind
}

// Alphabetical by title, then detail and key so equal titles keep a stable order. The
// order claims no popularity.
export function presentTracks<T extends Listed>(tracks: T[], showAll: boolean) {
  const compare = (a: string, b: string) =>
    a.localeCompare(b, undefined, { sensitivity: "base", numeric: true })
  return tracks
    .filter((t) => showAll || t.versionKind !== "alternative")
    .sort(
      (a, b) =>
        compare(a.title, b.title) ||
        compare(a.detail, b.detail) ||
        compare(a.key, b.key)
    )
}
