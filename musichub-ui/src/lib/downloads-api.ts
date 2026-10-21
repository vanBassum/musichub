// The persistent download queue, as the Downloads page reads it. The queue read API is not built yet, so this is
// the agreed shape (GET /api/downloads, newest first) and `listDownloadQueue` serves the sample below until the
// endpoint exists: then it becomes `getJson<DownloadQueueItem[]>("/api/downloads", "The downloads", signal)`.
export type DownloadQueueStatus = "queued" | "downloading" | "completed" | "failed"

export interface DownloadQueueItem {
  id: string
  trackTitle: string
  artist: string
  status: DownloadQueueStatus
  // 0 to 100 while the transfer reports it, otherwise null.
  progressPercent: number | null
  // Why a request failed, otherwise null.
  error: string | null
  createdAt: string
  updatedAt: string
  finishedAt: string | null
}

// Whether the request is still going to change, so the page keeps polling for it.
export const isDownloadActive = (item: DownloadQueueItem) =>
  item.status === "queued" || item.status === "downloading"

// Every endpoint-less value in this file is sample data: the page says so while it shows it.
export const downloadQueueIsSample = true

const minutesAgo = (minutes: number) =>
  new Date(Date.now() - minutes * 60_000).toISOString()

function sampleQueue(): DownloadQueueItem[] {
  return [
    {
      id: "5",
      trackTitle: "Teardrop",
      artist: "Massive Attack",
      status: "queued",
      progressPercent: null,
      error: null,
      createdAt: minutesAgo(1),
      updatedAt: minutesAgo(1),
      finishedAt: null,
    },
    {
      id: "4",
      trackTitle: "Glory Box",
      artist: "Portishead",
      status: "downloading",
      progressPercent: 42,
      error: null,
      createdAt: minutesAgo(6),
      updatedAt: minutesAgo(0),
      finishedAt: null,
    },
    {
      id: "3",
      trackTitle: "Angel",
      artist: "Massive Attack",
      status: "downloading",
      progressPercent: null,
      error: null,
      createdAt: minutesAgo(9),
      updatedAt: minutesAgo(2),
      finishedAt: null,
    },
    {
      id: "2",
      trackTitle: "Roads",
      artist: "Portishead",
      status: "completed",
      progressPercent: 100,
      error: null,
      createdAt: minutesAgo(50),
      updatedAt: minutesAgo(44),
      finishedAt: minutesAgo(44),
    },
    {
      id: "1",
      trackTitle: "Karma Police",
      artist: "Radiohead",
      status: "failed",
      progressPercent: 17,
      error: "The user went offline before the transfer finished.",
      createdAt: minutesAgo(120),
      updatedAt: minutesAgo(115),
      finishedAt: minutesAgo(115),
    },
  ]
}

export const listDownloadQueue = (signal?: AbortSignal) =>
  new Promise<DownloadQueueItem[]>((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException("Aborted", "AbortError"))
      return
    }
    resolve(sampleQueue())
  })
