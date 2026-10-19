import {
  fetchExternalArtists,
  fetchExternalTracks,
  type ExternalArtist,
  type ExternalTrack,
  type SearchFields,
} from "@/lib/catalog-api"
import { useDebouncedSearch, type SearchStatus } from "@/lib/use-debounced-search"

const delayMs = 400
const groupSize = 5

export type ExternalSearchStatus = SearchStatus

interface Groups {
  tracks: ExternalTrack[]
  artists?: ExternalArtist[]
}

async function search(
  term: string,
  fields: SearchFields,
  signal: AbortSignal
): Promise<Groups> {
  const [tracks, artists] = await Promise.all([
    fetchExternalTracks({ search: term, limit: groupSize, fields }, signal),
    fields.artist
      ? fetchExternalArtists({ search: term, limit: groupSize }, signal)
      : Promise.resolve(null),
  ])
  return { tracks: tracks.items, artists: artists?.items }
}

// Searches the external provider once typing pauses.
export function useExternalSearch(input: string, fields: SearchFields) {
  const { status, error, data, retry } = useDebouncedSearch(
    input,
    fields,
    delayMs,
    search
  )
  return {
    status,
    error,
    tracks: data?.tracks ?? [],
    artists: data?.artists ?? [],
    retry,
  }
}
