import { describe, expect, it } from "vitest"

import {
  formatLocation,
  formatSearchHash,
  parseLocation,
  parseSearchQuery,
} from "@/components/mobile-pages"

describe("settings location", () => {
  it("opens the section named in the hash", () => {
    const location = parseLocation("#/settings/jobs")
    expect(location).toMatchObject({
      tab: "more",
      screen: "settings",
      settingsSection: "jobs",
    })
    expect(formatLocation(location)).toBe("#/settings/jobs")
  })

  it("redirects the former More links to Settings", () => {
    expect(parseLocation("#/more/problems")).toMatchObject({
      tab: "more",
      screen: "settings",
      settingsSection: "problems",
    })
    expect(parseLocation("#/more/downloads").screen).toBe("downloads")
  })

  it("falls back to the first section", () => {
    expect(parseLocation("#/settings").settingsSection).toBe("files")
  })
})

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
