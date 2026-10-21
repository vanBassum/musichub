import { afterEach, describe, expect, it, vi } from "vitest"

import {
  formatDownloadStatus,
  isDownloadActive,
  parseDownloadStatus,
  listDownloadQueue,
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

  it("keeps polling only for requests that will still change", () => {
    expect(isDownloadActive(item({ status: "Queued" }))).toBe(true)
    expect(isDownloadActive(item({ status: "Downloading" }))).toBe(true)
    expect(isDownloadActive(item({ status: "Downloaded" }))).toBe(true)
    expect(isDownloadActive(item({ status: "Completed" }))).toBe(false)
    expect(isDownloadActive(item({ status: "Failed", error: "Offline." }))).toBe(false)
  })
})
