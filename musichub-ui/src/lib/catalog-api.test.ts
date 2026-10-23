import { afterEach, describe, expect, it, vi } from "vitest"

import { fetchTracks } from "@/lib/catalog-api"

afterEach(() => vi.unstubAllGlobals())

function stubFetch() {
  const fetchMock = vi.fn().mockResolvedValue(
    new Response(JSON.stringify({ items: [], total: 0, page: 1, pageSize: 25 }))
  )
  vi.stubGlobal("fetch", fetchMock)
  return fetchMock
}

const urlOf = (fetchMock: ReturnType<typeof stubFetch>) =>
  new URL(String(fetchMock.mock.calls[0][0]), "http://localhost")

describe("Library search", () => {
  it("sends the term with the audio filter and the page to the catalog only", async () => {
    const fetchMock = stubFetch()
    await fetchTracks(
      { search: "echo", audio: "without", page: 3 },
      new AbortController().signal
    )

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const url = urlOf(fetchMock)
    expect(url.pathname).toBe("/api/tracks")
    expect(url.searchParams.get("search")).toBe("echo")
    expect(url.searchParams.get("hasAudio")).toBe("false")
    expect(url.searchParams.get("page")).toBe("3")
  })

  it("leaves the term and the filter out once they are cleared", async () => {
    const fetchMock = stubFetch()
    await fetchTracks(
      { search: "", audio: "all", page: 1 },
      new AbortController().signal
    )

    const { searchParams } = urlOf(fetchMock)
    expect(searchParams.has("search")).toBe(false)
    expect(searchParams.has("hasAudio")).toBe(false)
  })
})
