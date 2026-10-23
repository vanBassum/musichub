import { describe, expect, it } from "vitest"

import {
  albumSubtitle,
  editionTrackToExternal,
  trackArtistLine,
  type EditionTrack,
  type ExternalAlbum,
} from "@/lib/album-api"

const album = (overrides: Partial<ExternalAlbum> = {}): ExternalAlbum => ({
  provider: "musicbrainz",
  externalId: "a1",
  title: "Now That's What I Call Music",
  disambiguation: null,
  url: "https://example.test/a1",
  type: "Album",
  secondaryTypes: [],
  firstReleaseDate: "2001-05-01",
  artists: [{ name: "Various Artists", externalId: null }],
  ...overrides,
})

const track = (overrides: Partial<EditionTrack> = {}): EditionTrack => ({
  position: 1,
  medium: 1,
  number: "1",
  title: "Teardrop",
  lengthMs: null,
  recordingId: "r1",
  recordingUrl: "https://example.test/r1",
  artists: [{ name: "Massive Attack", externalId: "m1" }],
  ...overrides,
})

describe("compilations", () => {
  const compilation = album({ secondaryTypes: ["Compilation"] })

  it("labels the album as a compilation with its year", () => {
    expect(albumSubtitle(compilation)).toBe(
      "Various Artists · Compilation · 2001"
    )
  })

  it("names each track's own artists", () => {
    expect(trackArtistLine(track(), compilation)).toBe("Massive Attack")
  })

  it("names the artists of a track that differs from a regular album's", () => {
    const regular = album({ artists: [{ name: "Portishead", externalId: null }] })
    expect(trackArtistLine(track(), regular)).toBe("Massive Attack")
  })

  it("leaves out artists that are just the album's", () => {
    const regular = album({
      artists: [{ name: "Massive Attack", externalId: null }],
    })
    expect(trackArtistLine(track(), regular)).toBeNull()
  })
})

describe("editionTrackToExternal", () => {
  it("keeps the track's own artists and provider identity", () => {
    expect(editionTrackToExternal(track(), "musicbrainz")).toEqual({
      provider: "musicbrainz",
      externalId: "r1",
      title: "Teardrop",
      disambiguation: null,
      url: "https://example.test/r1",
      artists: [{ name: "Massive Attack", externalId: "m1" }],
    })
  })

  it("skips a track the provider didn't name or identify", () => {
    expect(editionTrackToExternal(track({ title: null }), "musicbrainz")).toBeNull()
    expect(
      editionTrackToExternal(track({ recordingId: null }), "musicbrainz")
    ).toBeNull()
    expect(editionTrackToExternal(track({ artists: [] }), "musicbrainz")).toBeNull()
  })
})
