import { useCallback, useEffect, useRef, useState } from "react"

import {
  isDownloadActive,
  listDownloadQueue,
  removeDownload,
  retryDownload,
  type DownloadQueueItem,
} from "@/lib/downloads-api"
import { useLoader } from "@/lib/use-loader"

const pollMs = 5000

export interface DownloadQueueState {
  items: DownloadQueueItem[] | null
  // All requests in the queue, which can be more than `items` holds.
  total: number
  error: string | null
  refreshing: boolean
  refresh: () => Promise<void>
  // Requests with a retry or remove under way: their buttons are disabled, so a double click sends one call.
  busyIds: ReadonlySet<number>
  // Why the last retry or remove failed; the list was reloaded, so it shows the real state next to it.
  actionError: string | null
  retry: (id: number) => Promise<void>
  remove: (id: number) => Promise<void>
}

// Loads the download queue, and polls it while a request is queued or downloading. Retry and remove call the
// API and then reload the list: the page shows what the server holds, never a guess.
export function useDownloadQueue(): DownloadQueueState {
  const { data, error, refreshing, refresh, reload } = useLoader(listDownloadQueue)
  const [busyIds, setBusyIds] = useState<ReadonlySet<number>>(new Set())
  const [actionError, setActionError] = useState<string | null>(null)
  const inFlight = useRef(new Set<number>())

  const active = data?.items.some(isDownloadActive) ?? false
  useEffect(() => {
    if (!active) return
    const timer = setInterval(() => void reload(), pollMs)
    return () => clearInterval(timer)
  }, [active, reload])

  const act = useCallback(
    async (id: number, action: (id: number) => Promise<void>) => {
      if (inFlight.current.has(id)) return
      inFlight.current.add(id)
      setBusyIds(new Set(inFlight.current))
      setActionError(null)
      try {
        await action(id)
      } catch (failure) {
        setActionError(
          failure instanceof Error ? failure.message : "The action failed."
        )
      } finally {
        await reload()
        inFlight.current.delete(id)
        setBusyIds(new Set(inFlight.current))
      }
    },
    [reload]
  )

  const retry = useCallback((id: number) => act(id, retryDownload), [act])
  const remove = useCallback((id: number) => act(id, removeDownload), [act])

  return {
    items: data?.items ?? null,
    total: data?.total ?? 0,
    error,
    refreshing,
    refresh,
    busyIds,
    actionError,
    retry,
    remove,
  }
}
