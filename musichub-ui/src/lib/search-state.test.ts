import { describe, expect, it } from "vitest"

import { canShowMore, limitFor } from "@/lib/search-state"

describe("limitFor", () => {
  it("asks for a few of each kind in the overview", () => {
    expect(limitFor("albums", "all", 0)).toBe(5)
    expect(limitFor("tracks", "all", 3)).toBe(5)
  })

  it("asks only for the focused category, a page at a time", () => {
    expect(limitFor("tracks", "tracks", 0)).toBe(20)
    expect(limitFor("tracks", "tracks", 1)).toBe(40)
    expect(limitFor("albums", "tracks", 1)).toBe(0)
  })

  it("never asks for more than the API allows", () => {
    expect(limitFor("artists", "artists", 10)).toBe(100)
  })
})

describe("canShowMore", () => {
  it("offers more only when the answer filled its limit", () => {
    expect(canShowMore(20, 20)).toBe(true)
    expect(canShowMore(7, 20)).toBe(false)
  })

  it("stops at the maximum and for a kind that isn't shown", () => {
    expect(canShowMore(100, 100)).toBe(false)
    expect(canShowMore(0, 0)).toBe(false)
  })
})
