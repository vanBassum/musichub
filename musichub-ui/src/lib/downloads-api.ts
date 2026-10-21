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

// What the Downloads page narrows the queue to: every request, or one status.
export type DownloadStatusFilter = "all" | DownloadQueueStatus

export const downloadStatuses: DownloadQueueStatus[] = [
  "Queued",
  "Downloading",
  "Downloaded",
  "Completed",
  "Failed",
]

// The filter as a deep link carries it (`#/downloads?status=failed`).
export const formatDownloadStatus = (filter: DownloadStatusFilter) =>
  filter.toLowerCase()

// Anything that names no status is "all".
export const parseDownloadStatus = (value: string | null): DownloadStatusFilter =>
  downloadStatuses.find((status) => status.toLowerCase() === value) ?? "all"

// A page of the queue and the filter it was loaded for.
export interface FilteredDownloadQueuePage extends DownloadQueuePage {
  filter: DownloadStatusFilter
}

// The newest requests: the API caps a page at 100.
const pageSize = 100

// `total` counts the requests matching the filter, not the whole queue.
export const listDownloadQueue = async (
  filter: DownloadStatusFilter,
  signal?: AbortSignal
): Promise<FilteredDownloadQueuePage> => {
  const query = new URLSearchParams({ page: "1", pageSize: String(pageSize) })
  if (filter !== "all") query.set("status", filter)
  const page = await getJson<DownloadQueuePage>(
    `/api/downloads?${query}`,
    "The downloads",
    signal
  )
  return { ...page, filter }
}
