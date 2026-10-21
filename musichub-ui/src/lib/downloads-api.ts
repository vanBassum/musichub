import { getJson, problemDetail } from "@/lib/jobs-api"

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

// The manual actions the API allows, which it checks again itself: only a failed request is retried, and
// only a failed or completed one is removed (an active transfer cannot be cancelled).
export const canRetryDownload = (item: DownloadQueueItem) =>
  item.status === "Failed"

export const canRemoveDownload = (item: DownloadQueueItem) =>
  item.status === "Failed" || item.status === "Completed"

async function sendDownloadAction(
  url: string,
  method: "POST" | "DELETE",
  failure: string,
  acceptedStatus?: number
) {
  const response = await fetch(url, { method })
  if (response.ok || response.status === acceptedStatus) return
  throw new Error(
    (await problemDetail(response)) ?? `${failure} (HTTP ${response.status}).`
  )
}

// POST /api/downloads/{id}/retry: queues a failed request again. 409 (no longer failed, or the track already
// has an active download) is thrown with the API's explanation; nothing was changed then.
export const retryDownload = (id: number) =>
  sendDownloadAction(
    `/api/downloads/${id}/retry`,
    "POST",
    "The download could not be retried"
  )

// DELETE /api/downloads/{id}: takes the request out of the queue only. Safe to repeat: 404 means it is
// already gone, which is what was asked for.
export const removeDownload = (id: number) =>
  sendDownloadAction(
    `/api/downloads/${id}`,
    "DELETE",
    "The download could not be removed",
    404
  )

// The newest requests: the API caps a page at 100.
const pageSize = 100

export const listDownloadQueue = (signal?: AbortSignal) =>
  getJson<DownloadQueuePage>(
    `/api/downloads?page=1&pageSize=${pageSize}`,
    "The downloads",
    signal
  )
