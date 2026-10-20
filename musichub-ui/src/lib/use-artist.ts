import { useEffect, useState } from "react"

import {
  loadArtistInfo,
  loadArtistTracks,
  resolveArtist,
  type ArtistTarget,
} from "@/lib/artist-api"
import type { ArtistRef, SectionState } from "@/lib/artist"

interface Fetched<T> {
  // What the data or error belongs to; a different key means a newer request is loading.
  key: string
  data?: T
  error?: string
}

export interface Section<T> {
  state: SectionState<T>
  retry: () => void
}

// Loads one section. `arg` null means it can't start yet (loading). `arg` must be
// stable between renders and `load` a module-level function; nothing is cached.
function useSection<A, T>(
  arg: A | null,
  load: (arg: A, signal: AbortSignal) => Promise<T>
): Section<T> {
  const key = `${JSON.stringify(arg)}`
  const [attempt, setAttempt] = useState(0)
  const [result, setResult] = useState<Fetched<T> | null>(null)
  const fetchKey = `${key}|${attempt}`

  useEffect(() => {
    if (arg === null) return
    const controller = new AbortController()
    const { signal } = controller
    load(arg, signal)
      .then((data) => {
        if (!signal.aborted) setResult({ key: fetchKey, data })
      })
      .catch((error: unknown) => {
        if (signal.aborted) return
        setResult({
          key: fetchKey,
          error:
            error instanceof Error ? error.message : "Something went wrong.",
        })
      })
    return () => controller.abort()
  }, [arg, load, fetchKey])

  const state: SectionState<T> =
    arg === null || result?.key !== fetchKey
      ? { status: "loading" }
      : result.error !== undefined
        ? { status: "error", message: result.error }
        : { status: "ready", data: result.data as T }
  return { state, retry: () => setAttempt((n) => n + 1) }
}

// The data of the artist page. The catalog record behind the ref is found first;
// then the artist info and the tracks load independently, so one failing or slow
// source never hides the other. Reading never writes to the catalog.
export function useArtist(ref: ArtistRef | null) {
  const resolved = useSection(ref, resolveArtist)
  const target: ArtistTarget | null =
    resolved.state.status === "ready" ? resolved.state.data : null
  const info = useSection(target, loadArtistInfo)
  const tracks = useSection(target, loadArtistTracks)

  // A failed lookup of the catalog record stops both sections: say so in each.
  const failed = resolved.state.status === "error" ? resolved.state : null
  return {
    info: failed
      ? { state: failed, retry: resolved.retry }
      : info,
    tracks: failed
      ? { state: failed, retry: resolved.retry }
      : tracks,
  }
}
