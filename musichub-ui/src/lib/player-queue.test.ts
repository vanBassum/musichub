import { describe, expect, it } from "vitest"

import {
  clearExcept,
  insertAfter,
  moveEntry,
  neighbour,
  removeEntry,
} from "@/lib/player-queue"

const q = (...keys: number[]) => keys.map((key) => ({ key }))
const keys = (queue: { key: number }[]) => queue.map((entry) => entry.key)

describe("player queue", () => {
  it("inserts directly after the current entry, or at the front without one", () => {
    expect(keys(insertAfter(q(1, 2, 3), 2, { key: 9 }))).toEqual([1, 2, 9, 3])
    expect(keys(insertAfter(q(1, 2), null, { key: 9 }))).toEqual([9, 1, 2])
    expect(keys(insertAfter(q(1, 2), 7, { key: 9 }))).toEqual([9, 1, 2])
  })

  it("removes one entry", () => {
    expect(keys(removeEntry(q(1, 2, 3), 2))).toEqual([1, 3])
  })

  it("moves an entry by one and stops at the ends", () => {
    expect(keys(moveEntry(q(1, 2, 3), 2, -1))).toEqual([2, 1, 3])
    expect(keys(moveEntry(q(1, 2, 3), 2, 1))).toEqual([1, 3, 2])
    expect(keys(moveEntry(q(1, 2, 3), 1, -1))).toEqual([1, 2, 3])
    expect(keys(moveEntry(q(1, 2, 3), 3, 1))).toEqual([1, 2, 3])
  })

  it("finds the neighbours of the current entry", () => {
    expect(neighbour(q(1, 2, 3), 2, 1)?.key).toBe(3)
    expect(neighbour(q(1, 2, 3), 2, -1)?.key).toBe(1)
    expect(neighbour(q(1, 2, 3), 3, 1)).toBeUndefined()
    expect(neighbour(q(1, 2, 3), null, 1)).toBeUndefined()
  })

  it("clears everything but the current entry", () => {
    expect(keys(clearExcept(q(1, 2, 3), 2))).toEqual([2])
    expect(clearExcept(q(1, 2, 3), null)).toEqual([])
  })
})
