import { getJson } from "@/lib/jobs-api"

// The persistent download queue, as GET /api/downloads returns it (newest first).
export type DownloadQueueStatus =
  | "Queued"
  | "Downloading"
  | "Downloaded"
  | "Completed"
  | "Failed"

export interface DownloadQueueItem {
  id: number
  trackTitle: string | null
  artists: string[]
  fileName: string
  status: DownloadQueueStatus
  // 0 to 100 while the transfer reports it, otherwise null.
  progressPercent: number | null
  // Why a request failed (or the last attempt failed), otherwise null.
  error: string | null
  createdAt: string
  updatedAt: string
  finishedAt: string | null
}

export interface DownloadQueuePage {
  items: DownloadQueueItem[]
  total: number
}

// Whether the request is still going to change, so the page keeps polling for it.
export const isDownloadActive = (item: DownloadQueueItem) =>
  item.status === "Queued" ||
  item.status === "Downloading" ||
  item.status === "Downloaded"

// The newest requests: the API caps a page at 100.
const pageSize = 100

export const listDownloadQueue = (signal?: AbortSignal) =>
  getJson<DownloadQueuePage>(
    `/api/downloads?page=1&pageSize=${pageSize}`,
    "The downloads",
    signal
  )
