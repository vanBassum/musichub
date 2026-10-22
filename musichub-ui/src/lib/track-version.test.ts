import { describe, expect, it } from "vitest"

import { classifyVersion, presentTracks, type VersionKind } from "@/lib/track-version"

describe("classifyVersion", () => {
  it("treats a plain studio track (album or single) as regular", () => {
    expect(classifyVersion("Karma Police", null)).toBe("regular")
    expect(classifyVersion("Live and Let Die", null)).toBe("regular")
  })

  it("recognises a live recording", () => {
    expect(classifyVersion("Creep", "live, 1995-03-04: Wembley")).toBe("alternative")
    expect(classifyVersion("Creep (Live at Wembley)", null)).toBe("alternative")
  })

  it("recognises a remix and a demo", () => {
    expect(classifyVersion("Intro - Club Remix", null)).toBe("alternative")
    expect(classifyVersion("Intro", "demo")).toBe("alternative")
  })

  it("keeps an unrecognised qualifier ambiguous, not hidden", () => {
    expect(classifyVersion("Intro (2011 Remaster)", null)).toBe("ambiguous")
    expect(classifyVersion("Intro", "radio edit")).toBe("ambiguous")
  })
})

describe("presentTracks", () => {
  const track = (title: string, versionKind: VersionKind) => ({
    title,
    detail: "",
    key: title,
    versionKind,
  })
  const tracks = [
    track("Zebra", "regular"),
    track("apple (live)", "alternative"),
    track("Mango (Remaster)", "ambiguous"),
    track("Banana", "regular"),
  ]

  it("hides alternatives by default and sorts alphabetically", () => {
    expect(presentTracks([...tracks], false).map((t) => t.title)).toEqual([
      "Banana",
      "Mango (Remaster)",
      "Zebra",
    ])
  })

  it("shows every version, still alphabetical, on demand", () => {
    expect(presentTracks([...tracks], true).map((t) => t.title)).toEqual([
      "apple (live)",
      "Banana",
      "Mango (Remaster)",
      "Zebra",
    ])
  })
})
