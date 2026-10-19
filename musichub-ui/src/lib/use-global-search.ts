import { useEffect, useState } from "react"

import {
  fetchArtists,
  fetchTracks,
  type CatalogTrack,
  type SearchedArtist,
  type SearchFields,
} from "@/lib/catalog-api"

const delayMs = 300
const groupSize = 5

interface Result {
  // The term and fields the result belongs to; a different key means it is loading.
  key: string
  tracks?: CatalogTrack[]
  artists?: SearchedArtist[]
  error?: string
}

export type GlobalSearchStatus = "idle" | "loading" | "error" | "ready"

// Searches the local catalog once typing pauses. External result groups can be added
// beside `tracks` and `artists` later.
export function useGlobalSearch(input: string, fields: SearchFields) {
  const [term, setTerm] = useState("")
  const [result, setResult] = useState<Result | null>(null)
  const [attempt, setAttempt] = useState(0)
  const typed = input.trim()

  useEffect(() => {
    const timer = setTimeout(() => setTerm(typed), delayMs)
    return () => clearTimeout(timer)
  }, [typed])

  const key = `${term}|${fields.title}|${fields.artist}`

  useEffect(() => {
    if (term === "") return
    const controller = new AbortController()
    const { signal } = controller
    Promise.all([
      fetchTracks(
        { search: term, audio: "all", page: 1, pageSize: groupSize, fields },
        signal
      ),
      fields.artist
        ? fetchArtists({ search: term, pageSize: groupSize }, signal)
        : Promise.resolve(null),
    ])
      .then(([tracks, artists]) =>
        setResult({ key, tracks: tracks.items, artists: artists?.items })
      )
      .catch((error: unknown) => {
        if (signal.aborted) return
        setResult({
          key,
          error:
            error instanceof Error ? error.message : "Something went wrong.",
        })
      })
    return () => controller.abort()
  }, [term, fields, key, attempt])

  const status: GlobalSearchStatus =
    typed === ""
      ? "idle"
      : result?.key !== key || term !== typed
        ? "loading"
        : result.error
          ? "error"
          : "ready"

  return {
    status,
    tracks: result?.tracks ?? [],
    artists: result?.artists ?? [],
    retry: () => setAttempt((n) => n + 1),
  }
}
