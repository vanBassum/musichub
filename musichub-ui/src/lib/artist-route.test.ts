import { describe, expect, it } from "vitest"

import { formatArtistHash, parseArtistHash } from "@/lib/artist-route"

describe("artist deep link", () => {
  it("round-trips a local artist and its source", () => {
    const route = { ref: { kind: "local", id: 12 }, source: "library" } as const
    expect(parseArtistHash(formatArtistHash(route))).toEqual(route)
  })

  it("round-trips an external artist", () => {
    const route = {
      ref: {
        kind: "external",
        provider: "musicbrainz",
        externalId: "a/b c",
        name: "Björk & Co",
      },
      source: "search",
    } as const
    expect(parseArtistHash(formatArtistHash(route))).toEqual(route)
  })

  it("ignores other hashes and incomplete links", () => {
    expect(parseArtistHash("#/search?q=x")).toBeNull()
    expect(parseArtistHash("#/artist/local/abc")).toBeNull()
    expect(parseArtistHash("#/artist/external/musicbrainz")).toBeNull()
  })
})
