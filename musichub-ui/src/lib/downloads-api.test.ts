import { afterEach, describe, expect, it, vi } from "vitest"

import {
  isDownloadActive,
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

    await expect(listDownloadQueue()).resolves.toEqual(page)
    expect(fetchMock).toHaveBeenCalledWith("/api/downloads?page=1&pageSize=100", {
      signal: undefined,
    })
  })

  it("returns an empty page as it is, without inventing requests", async () => {
    vi.stubGlobal("fetch", async () =>
      Response.json({ items: [], total: 0, page: 1, pageSize: 100 })
    )

    const page = await listDownloadQueue()
    expect(page.items).toEqual([])
    expect(page.total).toBe(0)
  })

  it("fails with an explicit message when the API answers an error", async () => {
    vi.stubGlobal("fetch", async () => new Response("", { status: 500 }))

    await expect(listDownloadQueue()).rejects.toThrow(
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
