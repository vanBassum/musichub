import { useEffect, useState } from "react"

import {
  fetchTrack,
  fetchTracks,
  type AudioFilter,
  type CatalogTrack,
  type PagedTracks,
} from "@/lib/catalog-api"

interface Fetched<T> {
  // What the data or error belongs to; a different key means a newer request is loading.
  key: string
  data?: T
  error?: string
}

export type CatalogStatus = "loading" | "error" | "ready"

function message(error: unknown) {
  return error instanceof Error ? error.message : "Something went wrong."
}

export function useTracks(search: string, audio: AudioFilter, page: number) {
  const key = `${search}|${audio}|${page}`
  const [attempt, setAttempt] = useState(0)
  const [state, setState] = useState<Fetched<PagedTracks> | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetchTracks({ search, audio, page }, controller.signal)
      .then((data) => setState({ key, data }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) return
        // Keep the last page on screen behind the error.
        setState((previous) => ({
          key,
          data: previous?.data,
          error: message(error),
        }))
      })
    return () => controller.abort()
  }, [search, audio, page, key, attempt])

  const status: CatalogStatus =
    state?.key !== key ? "loading" : state.error ? "error" : "ready"
  return {
    status,
    data: state?.data,
    error: state?.error,
    retry: () => setAttempt((n) => n + 1),
  }
}

export function useTrack(id: number | null) {
  const key = String(id)
  const [state, setState] = useState<Fetched<CatalogTrack> | null>(null)

  useEffect(() => {
    if (id === null) return
    const controller = new AbortController()
    fetchTrack(id, controller.signal)
      .then((data) => setState({ key, data }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setState({ key, error: message(error) })
      })
    return () => controller.abort()
  }, [id, key])

  if (id === null || state?.key !== key) {
    return { status: "loading" as CatalogStatus }
  }
  return {
    status: (state.error ? "error" : "ready") as CatalogStatus,
    data: state.data,
    error: state.error,
  }
}
