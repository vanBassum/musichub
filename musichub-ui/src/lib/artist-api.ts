import {
  artistNames,
  catalogAvailability,
  getExternal,
  getJson,
  type CatalogTrack,
  type ExternalArtist,
  type ExternalTrack,
} from "@/lib/catalog-api"
import {
  providerLabel,
  type ArtistInfo,
  type ArtistRef,
  type ArtistTrack,
  type ArtistTracks,
} from "@/lib/artist"

interface ArtistDetail {
  id: number
  name: string
  trackCount: number
  sources: { source: string; externalId: string; url: string }[]
}

interface ExternalArtistTrackItem {
  track: ExternalTrack
  catalogTrackId: number | null
  hasAudio: boolean | null
}

// The artist asked for, and its catalog record when it has one. Both sections load
// from it.
export interface ArtistTarget {
  ref: ArtistRef
  local: ArtistDetail | null
}

const externalTrackLimit = 25

// Finds the catalog record behind a ref. An external artist has one only through a
// link a person or the identification confirmed (the server never follows a name);
// without a link it stays external and nothing is registered.
export async function resolveArtist(
  ref: ArtistRef,
  signal: AbortSignal
): Promise<ArtistTarget> {
  let localId: number | null
  if (ref.kind === "local") {
    localId = ref.id
  } else {
    const response = await fetch(
      `/api/artists/by-external/${encodeURIComponent(ref.provider)}/${encodeURIComponent(ref.externalId)}`,
      { signal }
    )
    if (response.status === 404) {
      localId = null
    } else if (response.ok) {
      localId = ((await response.json()) as { artistId: number }).artistId
    } else {
      throw new Error(`The server answered ${response.status}.`)
    }
  }
  const local =
    localId === null
      ? null
      : await getJson<ArtistDetail>(`/api/artists/${localId}`, signal)
  return { ref, local }
}

// The provider artist to read: the one asked for, or the MusicBrainz one a catalog
// artist is linked to.
function externalOf({ ref, local }: ArtistTarget) {
  if (ref.kind === "external") return ref.externalId
  return local?.sources.find((s) => s.source === "musicbrainz")?.externalId ?? null
}

export async function loadArtistInfo(
  target: ArtistTarget,
  signal: AbortSignal
): Promise<ArtistInfo> {
  const { local } = target
  const externalId = externalOf(target)

  let provided: ExternalArtist | null = null
  let externalUnavailable = false
  if (externalId) {
    try {
      provided = (
        await getExternal<{ artist: ExternalArtist }>(
          `/api/external/artists/${encodeURIComponent(externalId)}`,
          signal
        )
      ).artist
    } catch (error) {
      // A catalog artist still has its own page when the provider fails; an
      // external-only one has nothing else to show.
      if (local === null || signal.aborted) throw error
      externalUnavailable = true
    }
  }

  const sources = (local?.sources ?? []).map((s) => ({
    label: providerLabel(s.source),
    url: s.url,
  }))
  if (provided && !sources.some((s) => s.url === provided.url)) {
    sources.push({ label: providerLabel(provided.provider), url: provided.url })
  }
  return {
    name: local?.name ?? provided?.name ?? "",
    inCatalog: local !== null,
    sources,
    type: provided?.type ?? null,
    area: provided?.area ?? null,
    disambiguation: provided?.disambiguation ?? null,
    externalUnavailable,
  }
}

function localTrack(track: CatalogTrack): ArtistTrack {
  return {
    key: `local:${track.id}`,
    title: track.title,
    detail: [track.version, artistNames(track)].filter(Boolean).join(" · "),
    availability: catalogAvailability(track.hasAudio),
    provenance: null,
    addable: null,
  }
}

// A recording that is in the catalog (but not on this list) shows the catalog's
// availability; one that isn't is external, with the provider as provenance.
function externalTrack({
  track,
  catalogTrackId,
  hasAudio,
}: ExternalArtistTrackItem): ArtistTrack {
  const inCatalog = catalogTrackId !== null
  return {
    key: `${track.provider}:${track.externalId}`,
    title: track.disambiguation
      ? `${track.title} (${track.disambiguation})`
      : track.title,
    detail: track.artists.map((a) => a.name).join(", "),
    availability: !inCatalog ? "external" : catalogAvailability(hasAudio === true),
    provenance: inCatalog ? null : { label: providerLabel(track.provider), url: track.url },
    // In the catalog already: it is in the library, so no second record is made.
    addable: inCatalog ? null : track,
  }
}

// The tracks come from two sources that fail on their own: the catalog and the
// provider. One failing leaves the other's tracks on the page, with a warning. The
// provider supplies no popularity data, so the list is not ranked.
export async function loadArtistTracks(
  target: ArtistTarget,
  signal: AbortSignal
): Promise<ArtistTracks> {
  const externalId = externalOf(target)
  const [catalog, provided] = await Promise.allSettled([
    target.local === null
      ? Promise.resolve(null)
      : getJson<{ items: CatalogTrack[] }>(
          `/api/artists/${target.local.id}/tracks`,
          signal
        ),
    externalId
      ? getExternal<{ items: ExternalArtistTrackItem[] }>(
          `/api/external/artists/${encodeURIComponent(externalId)}/tracks?limit=${externalTrackLimit}`,
          signal
        )
      : Promise.resolve(null),
  ])
  if (catalog.status === "rejected" && provided.status === "rejected") {
    throw provided.reason
  }

  const items = catalog.status === "fulfilled" ? (catalog.value?.items ?? []) : []
  const known = new Set(items.map((t) => t.id))
  const tracks = items.map(localTrack)
  if (provided.status === "fulfilled" && provided.value) {
    for (const item of provided.value.items) {
      // A recording the catalog already has is that catalog track: listed once.
      if (item.catalogTrackId !== null && known.has(item.catalogTrackId)) continue
      tracks.push(externalTrack(item))
    }
  }

  const failed =
    catalog.status === "rejected"
      ? "the catalog"
      : provided.status === "rejected"
        ? providerLabel("musicbrainz")
        : null
  return {
    items: tracks,
    warning: failed ? `Tracks from ${failed} could not be loaded.` : null,
  }
}
