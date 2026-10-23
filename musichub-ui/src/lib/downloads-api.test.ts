import { afterEach, describe, expect, it, vi } from "vitest"

import {
  canRemoveDownload,
  canRetryDownload,
  downloadErrorHint,
  downloadFileName,
  formatDownloadStatus,
  isDownloadActive,
  parseDownloadStatus,
  listDownloadQueue,
  removeDownload,
  retryDownload,
  type DownloadQueueItem,
} from "@/lib/downloads-api"

const item = (overrides: Partial<DownloadQueueItem>): DownloadQueueItem => ({
  id: 1,
  trackTitle: "Teardrop",
  artists: ["Massive Attack"],
  fileName: "teardrop.flac",
  status: "Queued",
  progressPercent: null,
  error: null,
  createdAt: "2026-10-21T10:00:00Z",
  updatedAt: "2026-10-21T10:00:00Z",
  finishedAt: null,
  ...overrides,
})

afterEach(() => vi.unstubAllGlobals())

describe("download row details", () => {
  it("hints at an error with its first line, shortened", () => {
    expect(downloadErrorHint("slskd: 404\nstack trace")).toBe("slskd: 404")
    expect(downloadErrorHint("x".repeat(100))).toHaveLength(48)
    expect(downloadErrorHint("x".repeat(100)).endsWith("…")).toBe(true)
  })

  it("shows only the file name, never a directory", () => {
    expect(downloadFileName("C:\\music\\user\\a.flac")).toBe("a.flac")
    expect(downloadFileName("/home/user/a.flac")).toBe("a.flac")
    expect(downloadFileName("a.flac")).toBe("a.flac")
  })
})

describe("download queue api", () => {
  it("reads the newest page from the queue endpoint", async () => {
    const page = { items: [item({})], total: 1, page: 1, pageSize: 100 }
    const fetchMock = vi.fn(async () => Response.json(page))
    vi.stubGlobal("fetch", fetchMock)

    await expect(listDownloadQueue("all")).resolves.toEqual({ ...page, filter: "all" })
    expect(fetchMock).toHaveBeenCalledWith("/api/downloads?page=1&pageSize=100", {
      signal: undefined,
    })
  })

  it("asks the API for one status and reports the matching total", async () => {
    const page = { items: [item({ status: "Failed" })], total: 7, page: 1, pageSize: 100 }
    const fetchMock = vi.fn(async () => Response.json(page))
    vi.stubGlobal("fetch", fetchMock)

    const result = await listDownloadQueue("Failed")
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/downloads?page=1&pageSize=100&status=Failed",
      { signal: undefined }
    )
    expect(result.total).toBe(7)
    expect(result.filter).toBe("Failed")
  })

  it("round-trips a status through its deep link value", () => {
    expect(formatDownloadStatus("Failed")).toBe("failed")
    expect(parseDownloadStatus("failed")).toBe("Failed")
    expect(parseDownloadStatus("nonsense")).toBe("all")
    expect(parseDownloadStatus(null)).toBe("all")
  })

  it("returns an empty page as it is, without inventing requests", async () => {
    vi.stubGlobal("fetch", async () =>
      Response.json({ items: [], total: 0, page: 1, pageSize: 100 })
    )

    const page = await listDownloadQueue("all")
    expect(page.items).toEqual([])
    expect(page.total).toBe(0)
  })

  it("fails with an explicit message when the API answers an error", async () => {
    vi.stubGlobal("fetch", async () => new Response("", { status: 500 }))

    await expect(listDownloadQueue("all")).rejects.toThrow(
      "The downloads could not be loaded (HTTP 500)."
    )
  })

  it("offers retry only for a failed request and remove only for a final one", () => {
    const eligible = (status: DownloadQueueItem["status"]) => [
      canRetryDownload(item({ status })),
      canRemoveDownload(item({ status })),
    ]
    expect(eligible("Failed")).toEqual([true, true])
    expect(eligible("Completed")).toEqual([false, true])
    expect(eligible("Queued")).toEqual([false, false])
    expect(eligible("Downloading")).toEqual([false, false])
    expect(eligible("Downloaded")).toEqual([false, false])
    expect(eligible("Processing")).toEqual([false, false])
  })

  it("posts a retry and deletes a removal on the request's own address", async () => {
    const fetchMock = vi.fn(async () => new Response(null, { status: 204 }))
    vi.stubGlobal("fetch", fetchMock)

    await retryDownload(7)
    await removeDownload(7)

    expect(fetchMock).toHaveBeenNthCalledWith(1, "/api/downloads/7/retry", {
      method: "POST",
    })
    expect(fetchMock).toHaveBeenNthCalledWith(2, "/api/downloads/7", {
      method: "DELETE",
    })
  })

  it("shows the API's explanation when a retry or removal is refused", async () => {
    vi.stubGlobal("fetch", async () =>
      Response.json(
        { detail: "Only a failed download can be retried." },
        { status: 409 }
      )
    )
    await expect(retryDownload(7)).rejects.toThrow(
      "Only a failed download can be retried."
    )
    await expect(removeDownload(7)).rejects.toThrow(
      "Only a failed download can be retried."
    )
  })

  it("falls back to a message with the status when the API gives no explanation", async () => {
    vi.stubGlobal("fetch", async () => new Response("", { status: 500 }))

    await expect(retryDownload(7)).rejects.toThrow(
      "The download could not be retried (HTTP 500)."
    )
    await expect(removeDownload(7)).rejects.toThrow(
      "The download could not be removed (HTTP 500)."
    )
  })

  it("treats a retry of a request that is gone as an error and a removal as done", async () => {
    vi.stubGlobal("fetch", async () => new Response("", { status: 404 }))

    await expect(removeDownload(7)).resolves.toBeUndefined()
    await expect(retryDownload(7)).rejects.toThrow("(HTTP 404)")
  })

  it("keeps polling only for requests that will still change", () => {
    expect(isDownloadActive(item({ status: "Queued" }))).toBe(true)
    expect(isDownloadActive(item({ status: "Downloading" }))).toBe(true)
    expect(isDownloadActive(item({ status: "Downloaded" }))).toBe(true)
    expect(isDownloadActive(item({ status: "Processing" }))).toBe(true)
    expect(isDownloadActive(item({ status: "Completed" }))).toBe(false)
    expect(isDownloadActive(item({ status: "Failed", error: "Offline." }))).toBe(false)
  })
})
