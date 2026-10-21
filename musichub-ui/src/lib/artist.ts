import type { ExternalTrack } from "@/lib/catalog-api"
import type { VersionKind } from "@/lib/track-version"

// What the artist detail page shows. The page only knows these shapes; lib/artist-api.ts
// builds them from the catalog and the external provider.
export type Availability = "available" | "missing" | "external"

// Which artist to open. Both kinds open the same page: a local artist by its catalog
// id, an external one by the provider's own id (and the name it was found under).
export type ArtistRef =
  | { kind: "local"; id: number }
  | { kind: "external"; provider: string; externalId: string; name: string }

const providerLabels: Record<string, string> = { musicbrainz: "MusicBrainz" }

export function providerLabel(provider: string) {
  return providerLabels[provider] ?? provider
}

export interface ArtistInfo {
  name: string
  inCatalog: boolean
  // Where the external information, if any, comes from.
  sources: { label: string; url: string }[]
  type: string | null
  area: string | null
  disambiguation: string | null
  // The artist is in the catalog and linked to a provider, but the provider didn't answer.
  externalUnavailable: boolean
}

export interface ArtistTrack {
  key: string
  title: string
  detail: string
  versionKind: VersionKind
  availability: Availability
  // Set for a track that comes from the provider only.
  provenance: { label: string; url: string } | null
  // The provider data to add the track to the library; null when it is in the library
  // already.
  addable: ExternalTrack | null
}

export interface ArtistTracks {
  items: ArtistTrack[]
  // One source failed while the other answered: what is missing from the list.
  warning: string | null
}

// One section's data: each section loads, fails and empties on its own.
export type SectionState<T> =
  | { status: "loading" }
  | { status: "error"; message: string }
  | { status: "ready"; data: T }

export const availabilityLabels: Record<Availability, string> = {
  available: "Available",
  missing: "Missing",
  external: "External",
}
