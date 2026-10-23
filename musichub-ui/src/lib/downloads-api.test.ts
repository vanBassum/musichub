import { afterEach, describe, expect, it, vi } from "vitest"

import {
  canRemoveDownload,
  canRetryDownload,
  downloadErrorHint,
  removeDownload,
  retryDownload,
  type DownloadQueueStatus,
} from "@/lib/downloads-api"

afterEach(() => vi.unstubAllGlobals())

describe("download actions", () => {
  it("hints at an error with its first line, shortened", () => {
    expect(downloadErrorHint("slskd: 404\nstack trace")).toBe("slskd: 404")
    expect(downloadErrorHint("x".repeat(100))).toHaveLength(48)
    expect(downloadErrorHint("x".repeat(100)).endsWith("…")).toBe(true)
  })

  it("offers retry only for a failed request and remove only for a final one", () => {
    const eligible = (status: DownloadQueueStatus | null) => [
      canRetryDownload(status),
      canRemoveDownload(status),
    ]
    expect(eligible("Failed")).toEqual([true, true])
    expect(eligible("Completed")).toEqual([false, true])
    expect(eligible("Queued")).toEqual([false, false])
    expect(eligible("Downloading")).toEqual([false, false])
    expect(eligible("Downloaded")).toEqual([false, false])
    expect(eligible(null)).toEqual([false, false])
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
})
