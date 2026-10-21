import { useEffect } from "react"

import {
  isDownloadActive,
  listDownloadQueue,
  type DownloadQueueItem,
} from "@/lib/downloads-api"
import { useLoader } from "@/lib/use-loader"

const pollMs = 5000

export interface DownloadQueueState {
  items: DownloadQueueItem[] | null
  error: string | null
  refreshing: boolean
  refresh: () => Promise<void>
}

// Loads the download queue, and polls it while a request is queued or downloading.
export function useDownloadQueue(): DownloadQueueState {
  const { data, error, refreshing, refresh, reload } = useLoader(listDownloadQueue)

  const active = data?.some(isDownloadActive) ?? false
  useEffect(() => {
    if (!active) return
    const timer = setInterval(() => void reload(), pollMs)
    return () => clearInterval(timer)
  }, [active, reload])

  return { items: data, error, refreshing, refresh }
}
