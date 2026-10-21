import { useCallback, useEffect } from "react"

import {
  isDownloadActive,
  listDownloadQueue,
  type DownloadQueueItem,
  type DownloadStatusFilter,
} from "@/lib/downloads-api"
import { useLoader } from "@/lib/use-loader"

const pollMs = 5000

export interface DownloadQueueState {
  items: DownloadQueueItem[] | null
  // All requests matching the filter, which can be more than `items` holds.
  total: number
  error: string | null
  refreshing: boolean
  refresh: () => Promise<void>
}

// Loads the download queue for a status filter, and polls it while a request is queued or downloading. Right
// after the filter changes, `items` is null until the new filter's requests arrive.
export function useDownloadQueue(filter: DownloadStatusFilter): DownloadQueueState {
  const fetcher = useCallback(
    (signal: AbortSignal) => listDownloadQueue(filter, signal),
    [filter]
  )
  const { data: loaded, error, refreshing, refresh, reload } = useLoader(fetcher)
  const data = loaded?.filter === filter ? loaded : null

  const active = data?.items.some(isDownloadActive) ?? false
  useEffect(() => {
    if (!active) return
    const timer = setInterval(() => void reload(), pollMs)
    return () => clearInterval(timer)
  }, [active, reload])

  return {
    items: data?.items ?? null,
    total: data?.total ?? 0,
    error,
    refreshing,
    refresh,
  }
}
