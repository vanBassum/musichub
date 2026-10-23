import { useEffect, useState } from "react"

import type { SearchFields } from "@/lib/catalog-api"

interface Result<T> {
  // The term, fields and page the result belongs to; a different key means it is loading.
  key: string
  // The same without the page: pages with one query key add up to one list.
  queryKey: string
  items: T[]
  error?: string
}

export type SearchStatus = "idle" | "loading" | "error" | "ready"

// Runs `fetcher` once typing pauses for `delayMs`. A newer term or fields aborts the
// request in flight, and a result only counts for the key it was asked for, so a late
// answer can't replace a newer one. Page 0 starts the list; each later page is fetched
// at its offset and appended. Nothing is cached. `fetcher` must be a stable reference
// (define it at module level).
export function useDebouncedSearch<T>(
  input: string,
  fields: SearchFields,
  delayMs: number,
  fetcher: (
    term: string,
    fields: SearchFields,
    signal: AbortSignal,
    limit: number,
    offset: number
  ) => Promise<T[]>,
  // How many results one page asks for, and which page (show more asks the next).
  limit = 0,
  page = 0
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
  const queryKey = `${term}|${fields.title}|${fields.artist}|${limit}|${attempt}`
  const key = `${queryKey}|${page}`

  useEffect(() => {
    if (term === "") return
    const controller = new AbortController()
    const { signal } = controller
    fetcher(term, fields, signal, limit, page * limit)
      .then((items) => {
        if (signal.aborted) return
        setResult((previous) => ({
          key,
          queryKey,
          items:
            page > 0 && previous?.queryKey === queryKey
              ? [...previous.items, ...items]
              : items,
        }))
      })
      .catch((error: unknown) => {
        if (signal.aborted) return
        setResult({
          key,
          queryKey,
          items: [],
          error:
            error instanceof Error ? error.message : "Something went wrong.",
        })
      })
    return () => controller.abort()
  }, [term, fields, key, queryKey, fetcher, limit, page])

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
    items: result?.items ?? [],
    retry: () => setAttempt((n) => n + 1),
  }
}
