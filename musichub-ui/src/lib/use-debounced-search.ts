import { useEffect, useState } from "react"

import type { SearchFields } from "@/lib/catalog-api"

interface Result<T> {
  // The term and fields the result belongs to; a different key means it is loading.
  key: string
  data?: T
  error?: string
}

export type SearchStatus = "idle" | "loading" | "error" | "ready"

// Runs `fetcher` once typing pauses for `delayMs`. A newer term or fields aborts the
// request in flight, and a result only counts for the key it was asked for, so a late
// answer can't replace a newer one. Nothing is cached. `fetcher` must be a stable
// reference (define it at module level).
export function useDebouncedSearch<T>(
  input: string,
  fields: SearchFields,
  delayMs: number,
  fetcher: (
    term: string,
    fields: SearchFields,
    signal: AbortSignal,
    limit: number
  ) => Promise<T>,
  // How many results to ask for; a new limit loads again (show more).
  limit = 0
) {
  const [term, setTerm] = useState("")
  const [result, setResult] = useState<Result<T> | null>(null)
  const [attempt, setAttempt] = useState(0)
  const typed = input.trim()

  useEffect(() => {
    const timer = setTimeout(() => setTerm(typed), delayMs)
    return () => clearTimeout(timer)
  }, [typed, delayMs])

  // `attempt` is part of the key so a retry shows the loading state again.
  const key = `${term}|${fields.title}|${fields.artist}|${limit}|${attempt}`

  useEffect(() => {
    if (term === "") return
    const controller = new AbortController()
    const { signal } = controller
    fetcher(term, fields, signal, limit)
      .then((data) => {
        if (signal.aborted) return
        setResult({ key, data })
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
  }, [term, fields, key, attempt, fetcher, limit])

  const status: SearchStatus =
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
    data: result?.data,
    retry: () => setAttempt((n) => n + 1),
  }
}
