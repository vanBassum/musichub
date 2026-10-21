import { describe, expect, it } from "vitest"

import { formatArtistHash, type ArtistRoute } from "@/lib/artist-route"
import {
  activeNav,
  formatLocation,
  formatSearchHash,
  locationOf,
  parseLocation,
  parseSearchQuery,
} from "@/lib/navigation"

describe("search deep link", () => {
  it("round-trips a term", () => {
    const hash = formatSearchHash(" Björk & Co ")
    expect(parseSearchQuery(hash)).toBe("Björk & Co")
    expect(parseLocation(hash).tab).toBe("search")
  })

  it("has no query without a term or on another tab", () => {
    expect(formatSearchHash("  ")).toBe("#/search")
    expect(parseSearchQuery("#/search")).toBe("")
    expect(parseSearchQuery("#/player?q=x")).toBe("")
  })
})

describe("location", () => {
  it.each([
    ["#/library", locationOf("library")],
    ["#/downloads", locationOf("downloads")],
    ["#/player", locationOf("player")],
    ["#/player/queue", locationOf("player", null, "queue")],
    ["#/settings", locationOf("settings")],
    ["#/settings/files", locationOf("settings", "files")],
    ["#/settings/jobs", locationOf("settings", "jobs")],
    ["#/settings/problems", locationOf("settings", "problems")],
  ])("round-trips %s", (hash, location) => {
    expect(parseLocation(hash)).toEqual(location)
    expect(formatLocation(location)).toBe(hash)
  })

  it("opens Search for an empty or unknown hash", () => {
    expect(parseLocation("")).toEqual(locationOf("search"))
    expect(parseLocation("#/nowhere")).toEqual(locationOf("search"))
  })

  it("reopens Search with its term", () => {
    expect(formatLocation(locationOf("search"), "daft")).toBe(
      "#/search?q=daft"
    )
  })

  it("keeps the earlier #/more links working", () => {
    expect(parseLocation("#/more")).toEqual(locationOf("settings"))
    expect(parseLocation("#/more/files")).toEqual(
      locationOf("settings", "files")
    )
    expect(parseLocation("#/more/jobs")).toEqual(locationOf("settings", "jobs"))
    expect(parseLocation("#/more/problems")).toEqual(
      locationOf("settings", "problems")
    )
    expect(parseLocation("#/more/library")).toEqual(locationOf("library"))
    expect(parseLocation("#/more/downloads")).toEqual(locationOf("downloads"))
    expect(parseLocation("#/more/artists")).toEqual(locationOf("search"))
  })

  it("round-trips an artist link", () => {
    const artist: ArtistRoute = { ref: { kind: "local", id: 7 }, source: "library" }
    const hash = formatArtistHash(artist)
    const location = { ...locationOf("artist"), artist }
    expect(parseLocation(hash)).toEqual(location)
    expect(formatLocation(location)).toBe(hash)
  })

  it("opens Search for an artist link that names no artist", () => {
    expect(parseLocation("#/artist")).toEqual(locationOf("search"))
    expect(
      parseLocation("#/artist/external/musicbrainz/%E0%A4%A?name=x")
    ).toEqual(locationOf("search"))
  })

  it("shows an artist page under the page it came from", () => {
    const from = (source: ArtistRoute["source"]) =>
      activeNav({
        ...locationOf("artist"),
        artist: { ref: { kind: "local", id: 1 }, source },
      })
    expect(from("library")).toBe("library")
    expect(from("search")).toBe("search")
    expect(activeNav(locationOf("settings"))).toBe("settings")
  })
})
