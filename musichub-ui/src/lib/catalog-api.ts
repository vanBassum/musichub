export interface CatalogArtist {
  artistId: number
  name: string
  role: string | null
}

export interface CatalogTrack {
  id: number
  title: string
  version: string | null
  createdAt: string
  artists: CatalogArtist[]
  hasAudio: boolean
  // The user explicitly wants the track; independent of hasAudio.
  wanted: boolean
}

export interface PagedTracks {
  items: CatalogTrack[]
  total: number
  page: number
  pageSize: number
}

export interface SearchedArtist {
  id: number
  name: string
  trackCount: number
}

export interface ArtistList {
  items: SearchedArtist[]
  total: number
}

// The fields a track search can look in; "title" covers the title and the version.
export interface SearchFields {
  title: boolean
  artist: boolean
}

export type AudioFilter = "all" | "with" | "without"

export const pageSize = 25

export async function getJson<T>(url: string, signal: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal })
  if (!response.ok) {
    throw new Error(
      response.status === 404
        ? "Not found."
        : `The server answered ${response.status}.`
    )
  }
  return (await response.json()) as T
}

// The `fields` query value: the enabled search fields, comma separated.
function fieldsParam(fields: SearchFields) {
  return [fields.title && "title", fields.artist && "artist"]
    .filter(Boolean)
    .join(",")
}

export function fetchTracks(
  query: {
    search: string
    audio: AudioFilter
    page: number
    pageSize?: number
    fields?: SearchFields
  },
  signal: AbortSignal
) {
  const params = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize ?? pageSize),
  })
  if (query.search) params.set("search", query.search)
  if (query.fields) params.set("fields", fieldsParam(query.fields))
  if (query.audio !== "all") {
    params.set("hasAudio", String(query.audio === "with"))
  }
  return getJson<PagedTracks>(`/api/tracks?${params}`, signal)
}

export function fetchArtists(
  query: { search: string; pageSize: number },
  signal: AbortSignal
) {
  const params = new URLSearchParams({
    search: query.search,
    pageSize: String(query.pageSize),
  })
  return getJson<ArtistList>(`/api/artists?${params}`, signal)
}

export function fetchTrack(id: number, signal: AbortSignal) {
  return getJson<CatalogTrack>(`/api/tracks/${id}`, signal)
}

export function artistNames(track: CatalogTrack) {
  return track.artists.map((artist) => artist.name).join(", ")
}

export interface ExternalArtistCredit {
  name: string
  externalId: string | null
}

// External results are never catalog records: `provider` and `externalId` say where
// they came from and identify them stably.
export interface ExternalTrack {
  provider: string
  externalId: string
  title: string
  disambiguation: string | null
  url: string
  artists: ExternalArtistCredit[]
}

export interface ExternalArtist {
  provider: string
  externalId: string
  name: string
  disambiguation: string | null
  url: string
  type?: string | null
  area?: string | null
}

// The API answers 503 with a problem detail when the provider is missing, rate
// limited or unreachable; its text is the message.
export async function getExternal<T>(url: string, signal: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal })
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as {
      detail?: string
    } | null
    throw new Error(
      problem?.detail ?? `The server answered ${response.status}.`
    )
  }
  return (await response.json()) as T
}

export function fetchExternalTracks(
  query: { search: string; limit: number; fields: SearchFields },
  signal: AbortSignal
) {
  const params = new URLSearchParams({
    search: query.search,
    limit: String(query.limit),
    fields: fieldsParam(query.fields),
  })
  return getExternal<{ provider: string; items: ExternalTrack[] }>(
    `/api/external/tracks?${params}`,
    signal
  )
}

export interface WishlistResult {
  status: "added" | "alreadyWanted"
  track: CatalogTrack
  created: boolean
}

async function sendWish(url: string, init: RequestInit) {
  const response = await fetch(url, init)
  if (!response.ok) {
    throw new Error(`The server answered ${response.status}.`)
  }
  // Only a 200 or 201 that says the track is wanted counts: anything else is an error,
  // so the UI never shows Wanted for a track that is not.
  const result = (await response.json()) as Partial<WishlistResult>
  if (
    (response.status !== 200 && response.status !== 201) ||
    (result.status !== "added" && result.status !== "alreadyWanted") ||
    result.track?.wanted !== true
  ) {
    throw new Error("The server did not confirm the track as wanted.")
  }
  return result as WishlistResult
}

export function wantCatalogTrack(id: number) {
  return sendWish(`/api/tracks/${id}/wanted`, { method: "PUT" })
}

export function wantExternalTrack(track: ExternalTrack) {
  return sendWish("/api/tracks/wishlist/external", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(track),
  })
}

export function fetchExternalArtists(
  query: { search: string; limit: number },
  signal: AbortSignal
) {
  const params = new URLSearchParams({
    search: query.search,
    limit: String(query.limit),
  })
  return getExternal<{ provider: string; items: ExternalArtist[] }>(
    `/api/external/artists?${params}`,
    signal
  )
}
