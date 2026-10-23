import { describe, expect, it } from "vitest"

import { formatArtistHash, type ArtistRoute } from "@/lib/artist-route"
import {
  activeNav,
  formatJobsHash,
  formatLocation,
  formatSearchHash,
  locationOf,
  mobileMenuNav,
  mobileNav,
  parseJobsFilter,
  parseLocation,
  parseSearchQuery,
  primaryNav,
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

describe("jobs history deep link", () => {
  it.each(["failed", "stuck"] as const)("round-trips the %s filter", (filter) => {
    const hash = formatJobsHash(filter)
    expect(hash).toBe(`#/jobs?state=${filter}`)
    expect(parseJobsFilter(hash)).toBe(filter)
    expect(parseLocation(hash)).toEqual(locationOf("jobs"))
  })

  it("lists all runs without a valid filter or on another screen", () => {
    expect(formatJobsHash("all")).toBe("#/jobs")
    expect(parseJobsFilter("#/jobs")).toBe("all")
    expect(parseJobsFilter("#/jobs?state=bogus")).toBe("all")
    expect(parseJobsFilter("#/files?state=failed")).toBe("all")
  })

  it("still reads the earlier Settings link", () => {
    expect(parseJobsFilter("#/settings/jobs?state=failed")).toBe("failed")
  })
})

describe("navigation entries", () => {
  it("lists the six destinations in the sidebar and all of them on the phone", () => {
    expect(primaryNav).toEqual([
      "search",
      "library",
      "wishlist",
      "files",
      "jobs",
      "settings",
    ])
    expect([...mobileNav, ...mobileMenuNav].sort()).toEqual(
      [...primaryNav, "player"].sort()
    )
  })

  it("highlights Files and Jobs as their own destinations", () => {
    expect(activeNav(locationOf("files"))).toBe("files")
    expect(activeNav(locationOf("jobs"))).toBe("jobs")
  })
})

describe("location", () => {
  it.each([
    ["#/library", locationOf("library")],
    ["#/wishlist", locationOf("wishlist")],
    ["#/player", locationOf("player")],
    ["#/player/queue", locationOf("player", "queue")],
    ["#/settings", locationOf("settings")],
    ["#/files", locationOf("files")],
    ["#/jobs", locationOf("jobs")],
  ])("round-trips %s", (hash, location) => {
    expect(parseLocation(hash)).toEqual(location)
    expect(formatLocation(location)).toBe(hash)
  })

  it("round-trips the Library and Wishlist filters", () => {
    const missing = { ...locationOf("library"), audio: "without" as const }
    expect(parseLocation("#/library?audio=without")).toEqual(missing)
    expect(formatLocation(missing)).toBe("#/library?audio=without")

    const notFound = { ...locationOf("wishlist"), wishlistFilter: "NotFound" as const }
    expect(parseLocation("#/wishlist?state=notfound")).toEqual(notFound)
    expect(formatLocation(notFound)).toBe("#/wishlist?state=notfound")
  })

  it("ignores an unknown filter value and filters of another page", () => {
    expect(parseLocation("#/library?audio=x")).toEqual(locationOf("library"))
    expect(parseLocation("#/wishlist?state=x")).toEqual(locationOf("wishlist"))
    expect(parseLocation("#/wishlist?audio=without")).toEqual(locationOf("wishlist"))
  })

  it("opens the Wishlist for the earlier Downloads links, keeping a status it still has", () => {
    expect(parseLocation("#/downloads")).toEqual(locationOf("wishlist"))
    expect(parseLocation("#/downloads?status=x")).toEqual(locationOf("wishlist"))
    expect(parseLocation("#/downloads?status=failed")).toEqual({
      ...locationOf("wishlist"),
      wishlistFilter: "Failed",
    })
  })

  it("opens Files and Jobs for the earlier Settings links", () => {
    expect(parseLocation("#/settings/files")).toEqual(locationOf("files"))
    expect(parseLocation("#/settings/jobs")).toEqual(locationOf("jobs"))
  })

  it("opens Settings for the removed Problems links", () => {
    expect(parseLocation("#/settings/problems")).toEqual(locationOf("settings"))
    expect(parseLocation("#/more/problems")).toEqual(locationOf("settings"))
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
    expect(parseLocation("#/more/files")).toEqual(locationOf("files"))
    expect(parseLocation("#/more/jobs")).toEqual(locationOf("jobs"))
    expect(parseLocation("#/more/library")).toEqual(locationOf("library"))
    expect(parseLocation("#/more/downloads")).toEqual(locationOf("wishlist"))
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
