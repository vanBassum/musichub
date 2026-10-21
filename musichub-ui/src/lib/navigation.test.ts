import { describe, expect, it } from "vitest"

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
    ["#/artist", locationOf("artist")],
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

  it("shows an artist page under Search", () => {
    expect(activeNav("artist")).toBe("search")
    expect(activeNav("settings")).toBe("settings")
  })
})
