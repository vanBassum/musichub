import { fetchExternalAlbums } from "@/lib/album-api"
import {
  fetchExternalArtists,
  fetchExternalTracks,
  type SearchFields,
} from "@/lib/catalog-api"
import { useDebouncedSearch } from "@/lib/use-debounced-search"

const delayMs = 400

// A limit of 0 means the kind isn't shown: nothing is asked of the provider.
const searchAlbums = async (
  term: string,
  _fields: SearchFields,
  signal: AbortSignal,
  limit: number
) =>
  limit === 0
    ? []
    : (await fetchExternalAlbums({ search: term, limit }, signal)).items

const searchTracks = async (
  term: string,
  fields: SearchFields,
  signal: AbortSignal,
  limit: number
) =>
  limit === 0
    ? []
    : (await fetchExternalTracks({ search: term, limit, fields }, signal)).items

const searchArtists = async (
  term: string,
  fields: SearchFields,
  signal: AbortSignal,
  limit: number
) =>
  limit === 0 || !fields.artist
    ? []
    : (await fetchExternalArtists({ search: term, limit }, signal)).items

export interface ExternalLimits {
  albums: number
  tracks: number
  artists: number
}

// Searches the external provider once typing pauses: albums, tracks and artists
// each on their own, so one failing or slow never hides the others.
export function useExternalSearch(
  input: string,
  fields: SearchFields,
  limits: ExternalLimits
) {
  const albums = useDebouncedSearch(input, fields, delayMs, searchAlbums, limits.albums)
  const tracks = useDebouncedSearch(input, fields, delayMs, searchTracks, limits.tracks)
  const artists = useDebouncedSearch(input, fields, delayMs, searchArtists, limits.artists)
  return {
    albums: { ...albums, items: albums.data ?? [] },
    tracks: { ...tracks, items: tracks.data ?? [] },
    artists: { ...artists, items: artists.data ?? [] },
  }
}
