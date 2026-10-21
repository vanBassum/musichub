import { describe, expect, it } from "vitest"

import { layoutFor } from "@/lib/layout"

describe("layoutFor", () => {
  it("is a phone below the tablet width", () => {
    expect(layoutFor(24)).toBe("phone")
    expect(layoutFor(47.99)).toBe("phone")
  })

  it("is a tablet from the tablet width up to the desktop width", () => {
    expect(layoutFor(48)).toBe("tablet")
    expect(layoutFor(63.99)).toBe("tablet")
  })

  it("is a desktop from the desktop width", () => {
    expect(layoutFor(64)).toBe("desktop")
    expect(layoutFor(120)).toBe("desktop")
  })
})
