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
}

export interface PagedTracks {
  items: CatalogTrack[]
  total: number
  page: number
  pageSize: number
}

export type AudioFilter = "all" | "with" | "without"

export const pageSize = 25

async function getJson<T>(url: string, signal: AbortSignal): Promise<T> {
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

export function fetchTracks(
  query: { search: string; audio: AudioFilter; page: number },
  signal: AbortSignal
) {
  const params = new URLSearchParams({
    page: String(query.page),
    pageSize: String(pageSize),
  })
  if (query.search) params.set("search", query.search)
  if (query.audio !== "all") {
    params.set("hasAudio", String(query.audio === "with"))
  }
  return getJson<PagedTracks>(`/api/tracks?${params}`, signal)
}

export function fetchTrack(id: number, signal: AbortSignal) {
  return getJson<CatalogTrack>(`/api/tracks/${id}`, signal)
}

export function artistNames(track: CatalogTrack) {
  return track.artists.map((artist) => artist.name).join(", ")
}
