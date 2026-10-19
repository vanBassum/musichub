import {
  fetchArtists,
  fetchTracks,
  type CatalogTrack,
  type SearchedArtist,
  type SearchFields,
} from "@/lib/catalog-api"
import { useDebouncedSearch, type SearchStatus } from "@/lib/use-debounced-search"

const delayMs = 300
const groupSize = 5

export type GlobalSearchStatus = SearchStatus

interface Groups {
  tracks: CatalogTrack[]
  artists?: SearchedArtist[]
}

async function search(
  term: string,
  fields: SearchFields,
  signal: AbortSignal
): Promise<Groups> {
  const [tracks, artists] = await Promise.all([
    fetchTracks(
      { search: term, audio: "all", page: 1, pageSize: groupSize, fields },
      signal
    ),
    fields.artist
      ? fetchArtists({ search: term, pageSize: groupSize }, signal)
      : Promise.resolve(null),
  ])
  return { tracks: tracks.items, artists: artists?.items }
}

// Searches the local catalog once typing pauses.
export function useGlobalSearch(input: string, fields: SearchFields) {
  const { status, data, retry } = useDebouncedSearch(
    input,
    fields,
    delayMs,
    search
  )
  return {
    status,
    tracks: data?.tracks ?? [],
    artists: data?.artists ?? [],
    retry,
  }
}
