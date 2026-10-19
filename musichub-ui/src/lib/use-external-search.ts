import { useEffect, useState } from "react"

import {
  fetchExternalArtists,
  fetchExternalTracks,
  type ExternalArtist,
  type ExternalTrack,
  type SearchFields,
} from "@/lib/catalog-api"

const delayMs = 400
const groupSize = 5

interface Result {
  // The term and fields the result belongs to; a different key means it is loading.
  key: string
  tracks?: ExternalTrack[]
  artists?: ExternalArtist[]
  error?: string
}

export type ExternalSearchStatus = "idle" | "loading" | "error" | "ready"

// Searches the external provider once typing pauses. A newer term or fields aborts the
// request in flight, and a result only counts for the key it was asked for, so a late
// answer can't replace a newer one. Nothing is cached.
export function useExternalSearch(input: string, fields: SearchFields) {
  const [term, setTerm] = useState("")
  const [result, setResult] = useState<Result | null>(null)
  const [attempt, setAttempt] = useState(0)
  const typed = input.trim()

  useEffect(() => {
    const timer = setTimeout(() => setTerm(typed), delayMs)
    return () => clearTimeout(timer)
  }, [typed])

  // `attempt` is part of the key so a retry shows the loading state again.
  const key = `${term}|${fields.title}|${fields.artist}|${attempt}`

  useEffect(() => {
    if (term === "") return
    const controller = new AbortController()
    const { signal } = controller
    Promise.all([
      fetchExternalTracks({ search: term, limit: groupSize, fields }, signal),
      fields.artist
        ? fetchExternalArtists({ search: term, limit: groupSize }, signal)
        : Promise.resolve(null),
    ])
      .then(([tracks, artists]) => {
        if (signal.aborted) return
        setResult({ key, tracks: tracks.items, artists: artists?.items })
      })
      .catch((error: unknown) => {
        if (signal.aborted) return
        setResult({
          key,
          error:
            error instanceof Error ? error.message : "Something went wrong.",
        })
      })
    return () => controller.abort()
  }, [term, fields, key])

  const status: ExternalSearchStatus =
    typed === ""
      ? "idle"
      : result?.key !== key || term !== typed
        ? "loading"
        : result.error
          ? "error"
          : "ready"

  return {
    status,
    error: result?.error,
    tracks: result?.tracks ?? [],
    artists: result?.artists ?? [],
    retry: () => setAttempt((n) => n + 1),
  }
}
