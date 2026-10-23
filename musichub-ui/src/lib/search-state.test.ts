import { describe, expect, it } from "vitest"

import { canShowMore, limitFor } from "@/lib/search-state"

describe("limitFor", () => {
  it("asks for a few of each kind in the overview", () => {
    expect(limitFor("albums", "all")).toBe(5)
    expect(limitFor("tracks", "all")).toBe(5)
  })

  it("asks only for the focused category, a page at a time", () => {
    expect(limitFor("tracks", "tracks")).toBe(20)
    expect(limitFor("albums", "tracks")).toBe(0)
  })
})

describe("canShowMore", () => {
  it("offers more only when every page so far came back full", () => {
    expect(canShowMore(20, 20, 0)).toBe(true)
    expect(canShowMore(40, 20, 1)).toBe(true)
    expect(canShowMore(7, 20, 0)).toBe(false)
    expect(canShowMore(33, 20, 1)).toBe(false)
  })

  it("has nothing more for a kind that isn't shown", () => {
    expect(canShowMore(0, 0, 0)).toBe(false)
  })
})
