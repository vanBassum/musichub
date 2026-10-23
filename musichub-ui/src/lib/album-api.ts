import {
  getExternal,
  type ExternalArtistCredit,
  type ExternalTrack,
} from "@/lib/catalog-api"

// An album as the external provider names it: the release group that all of its
// editions belong to. Never a catalog record.
export interface ExternalAlbum {
  provider: string
  externalId: string
  title: string
  disambiguation: string | null
  url: string
  type: string | null
  secondaryTypes: string[]
  firstReleaseDate: string | null
  artists: ExternalArtistCredit[]
}

export interface ExternalEdition {
  provider: string
  externalId: string
  title: string
  disambiguation: string | null
  url: string
  date: string | null
  country: string | null
  status: string | null
  packaging: string | null
  barcode: string | null
  trackCount: number | null
}

export interface AlbumEditions {
  album: ExternalAlbum
  editions: ExternalEdition[]
  // The provider has more editions than are listed.
  truncated: boolean
  // The edition the evidence singles out, if any; null when the user has to choose.
  suggestion: { editionId: string | null; reason: string }
}

export interface EditionTrack {
  position: number
  medium: number
  number: string | null
  title: string | null
  lengthMs: number | null
  recordingId: string | null
  recordingUrl: string | null
  artists: ExternalArtistCredit[]
}

export interface EditionTracks {
  edition: ExternalEdition
  state: "Complete" | "Incomplete" | "Missing"
  problems: string[]
  tracks: EditionTrack[]
}

export function fetchExternalAlbums(
  query: { search: string; limit: number },
  signal: AbortSignal
) {
  const params = new URLSearchParams({
    search: query.search,
    limit: String(query.limit),
  })
  return getExternal<{ provider: string; items: ExternalAlbum[] }>(
    `/api/external/albums?${params}`,
    signal
  )
}

export function fetchAlbumEditions(albumId: string, signal: AbortSignal) {
  return getExternal<AlbumEditions>(
    `/api/external/albums/${encodeURIComponent(albumId)}/editions`,
    signal
  )
}

export async function fetchEditionTracks(
  editionId: string,
  signal: AbortSignal
): Promise<EditionTracks> {
  const response = await getExternal<{ provider: string; edition: EditionTracks }>(
    `/api/external/editions/${encodeURIComponent(editionId)}/tracks`,
    signal
  )
  return response.edition
}

export const isCompilation = (album: ExternalAlbum) =>
  album.secondaryTypes.some((type) => type.toLowerCase() === "compilation")

export const creditNames = (artists: ExternalArtistCredit[]) =>
  artists.map((artist) => artist.name).join(", ")

// What an album result says it is below its title: its artists, its kind and year.
export function albumSubtitle(album: ExternalAlbum) {
  const kind = isCompilation(album) ? "Compilation" : (album.type ?? "Album")
  return [creditNames(album.artists), kind, album.firstReleaseDate?.slice(0, 4)]
    .filter(Boolean)
    .join(" · ")
}

// A track of an edition shows its own artists when they are not simply the album's
// (always on a compilation), so a Various Artists album names who plays what.
export function trackArtistLine(track: EditionTrack, album: ExternalAlbum) {
  const names = creditNames(track.artists)
  if (names === "") return null
  return isCompilation(album) || names !== creditNames(album.artists)
    ? names
    : null
}

// The track as the library takes it. Only a track the provider named and identified
// can be added: nothing is filled in from elsewhere.
export function editionTrackToExternal(
  track: EditionTrack,
  provider: string
): ExternalTrack | null {
  if (!track.title || !track.recordingId || track.artists.length === 0) {
    return null
  }
  return {
    provider,
    externalId: track.recordingId,
    title: track.title,
    disambiguation: null,
    url: track.recordingUrl ?? "",
    artists: track.artists,
  }
}

export function editionLabel(edition: ExternalEdition) {
  return [
    edition.disambiguation ?? edition.title,
    edition.date?.slice(0, 4),
    edition.country,
    edition.packaging ?? edition.status,
    edition.trackCount === null ? null : `${edition.trackCount} tracks`,
  ]
    .filter(Boolean)
    .join(" · ")
}
