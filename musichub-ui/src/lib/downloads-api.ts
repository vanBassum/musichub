import { problemDetail } from "@/lib/jobs-api"

// The status of a request in the persistent download queue, as the API reports it.
export type DownloadQueueStatus =
  | "Queued"
  | "Downloading"
  | "Downloaded"
  | "Processing"
  | "Completed"
  | "Failed"

// The manual actions the API allows, which it checks again itself: only a failed request is retried, and
// only a failed or completed one is removed (an active transfer cannot be cancelled).
export const canRetryDownload = (status: DownloadQueueStatus | null) =>
  status === "Failed"

export const canRemoveDownload = (status: DownloadQueueStatus | null) =>
  status === "Failed" || status === "Completed"

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

// A one-line hint of why a request failed: the first line of the error, shortened. The full text is in the
// row's details.
export const downloadErrorHint = (error: string, maxLength = 48) => {
  const line = (error.split(/\r?\n/, 1)[0] ?? "").trim()
  return line.length > maxLength ? `${line.slice(0, maxLength - 1)}…` : line
}
