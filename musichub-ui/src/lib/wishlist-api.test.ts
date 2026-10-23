import { afterEach, describe, expect, it, vi } from "vitest"

import {
  formatWishlistFilter,
  getTrackActivity,
  isWishlistBusy,
  listWishlist,
  parseWishlistFilter,
  searchNow,
  wishlistStates,
  type WishlistItem,
  type WishlistPage,
} from "@/lib/wishlist-api"

const item = (overrides: Partial<WishlistItem>): WishlistItem => ({
  trackId: 1,
  title: "Teardrop",
  version: null,
  artists: ["Massive Attack"],
  state: "Missing",
  canSearch: true,
  lastActivityAt: null,
  lastActivity: null,
  downloadRequestId: null,
  downloadStatus: null,
  error: null,
  ...overrides,
})

const counts = Object.fromEntries(
  wishlistStates.map((state) => [state, 0])
) as WishlistPage["counts"]

afterEach(() => vi.unstubAllGlobals())

describe("wishlist api", () => {
  it("reads the newest page and tags it with the filter it was loaded for", async () => {
    const page = { items: [item({})], total: 1, counts, page: 1, pageSize: 100 }
    const fetchMock = vi.fn(async () => Response.json(page))
    vi.stubGlobal("fetch", fetchMock)

    await expect(listWishlist("all")).resolves.toEqual({ ...page, filter: "all" })
    expect(fetchMock).toHaveBeenCalledWith("/api/wishlist?page=1&pageSize=100", {
      signal: undefined,
    })
  })

  it("asks the API for one state and reports the matching total", async () => {
    const page = { items: [], total: 4, counts, page: 1, pageSize: 100 }
    const fetchMock = vi.fn(async () => Response.json(page))
    vi.stubGlobal("fetch", fetchMock)

    const result = await listWishlist("NotFound")
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/wishlist?page=1&pageSize=100&state=NotFound",
      { signal: undefined }
    )
    expect(result.total).toBe(4)
    expect(result.filter).toBe("NotFound")
  })

  it("returns an empty overview as it is, without inventing tracks", async () => {
    vi.stubGlobal("fetch", async () =>
      Response.json({ items: [], total: 0, counts, page: 1, pageSize: 100 })
    )

    const page = await listWishlist("all")
    expect(page.items).toEqual([])
    expect(page.total).toBe(0)
  })

  it("fails with an explicit message when the API answers an error", async () => {
    vi.stubGlobal("fetch", async () => new Response("", { status: 500 }))

    await expect(listWishlist("all")).rejects.toThrow(
      "The wishlist could not be loaded (HTTP 500)."
    )
  })

  it("round-trips a state through its deep link value", () => {
    expect(formatWishlistFilter("NotFound")).toBe("notfound")
    expect(parseWishlistFilter("notfound")).toBe("NotFound")
    expect(parseWishlistFilter("nonsense")).toBe("all")
    expect(parseWishlistFilter(null)).toBe("all")
  })

  it("keeps polling only while a track will still change on its own", () => {
    const busy = (state: WishlistItem["state"]) => isWishlistBusy(item({ state }))
    expect(busy("Searching")).toBe(true)
    expect(busy("Queued")).toBe(true)
    expect(busy("Downloading")).toBe(true)
    expect(busy("Downloaded")).toBe(true)
    expect(busy("Missing")).toBe(false)
    expect(busy("NotFound")).toBe(false)
    expect(busy("Failed")).toBe(false)
    expect(busy("Available")).toBe(false)
  })
})

describe("search now", () => {
  it("posts to the track's own search address", async () => {
    const fetchMock = vi.fn(async () => Response.json({ executionId: "9" }, { status: 202 }))
    vi.stubGlobal("fetch", fetchMock)

    await searchNow(7)

    expect(fetchMock).toHaveBeenCalledWith("/api/wishlist/7/search", {
      method: "POST",
    })
  })

  it("shows the API's explanation when the search is refused", async () => {
    vi.stubGlobal("fetch", async () =>
      Response.json({ detail: "A search is already under way." }, { status: 409 })
    )

    await expect(searchNow(7)).rejects.toThrow("A search is already under way.")
  })

  it("falls back to a message with the status when the API gives no explanation", async () => {
    vi.stubGlobal("fetch", async () => new Response("", { status: 503 }))

    await expect(searchNow(7)).rejects.toThrow(
      "The search could not be started (HTTP 503)."
    )
  })
})

describe("track activity", () => {
  it("reads the recorded timeline of one track", async () => {
    const page = { items: [], total: 0, page: 1, pageSize: 200 }
    const fetchMock = vi.fn(async () => Response.json(page))
    vi.stubGlobal("fetch", fetchMock)

    await expect(getTrackActivity("7")).resolves.toEqual(page)
    expect(fetchMock).toHaveBeenCalledWith("/api/tracks/7/activity?pageSize=200", {
      signal: undefined,
    })
  })
})
