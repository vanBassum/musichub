import type { ArtistRef } from "@/lib/artist"

// Where an artist page was opened from; Back returns there.
export type ArtistSource = "search" | "library"

// The label of the Back button on an artist page opened from `source`.
export const artistBackLabel: Record<ArtistSource, string> = {
  search: "Search",
  library: "Library",
}

export interface ArtistRoute {
  ref: ArtistRef
  source: ArtistSource
}

// An artist deep link is `#/artist/local/<id>` or
// `#/artist/external/<provider>/<externalId>?name=<name>`, each with `from=<source>`.
export function formatArtistHash({ ref, source }: ArtistRoute): string {
  const params = new URLSearchParams()
  if (ref.kind === "external") params.set("name", ref.name)
  params.set("from", source)
  const path =
    ref.kind === "local"
      ? `local/${ref.id}`
      : `external/${encodeURIComponent(ref.provider)}/${encodeURIComponent(ref.externalId)}`
  return `#/artist/${path}?${params}`
}

// null when the hash is not an artist link, or one that doesn't name an artist.
export function parseArtistHash(hash: string): ArtistRoute | null {
  const [path, query = ""] = hash.replace(/^#\/?/, "").split("?")
  const [root, kind, first, second] = path.split("/")
  if (root !== "artist") return null
  const params = new URLSearchParams(query)
  const source: ArtistSource =
    params.get("from") === "library" ? "library" : "search"

  if (kind === "local" && /^\d+$/.test(first ?? "")) {
    return { ref: { kind: "local", id: Number(first) }, source }
  }
  if (kind === "external" && first && second) {
    const provider = decodeURIComponent(first)
    const externalId = decodeURIComponent(second)
    const name = params.get("name") ?? ""
    return { ref: { kind: "external", provider, externalId, name }, source }
  }
  return null
}

export function openArtist(route: ArtistRoute) {
  window.location.hash = formatArtistHash(route)
}
