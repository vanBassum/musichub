import { describe, expect, it } from "vitest"

import {
  appendEntry,
  clearExcept,
  insertAfter,
  isQueued,
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

  it("appends repeated fast additions in order, duplicates included", () => {
    const entry = (key: number, id: string) => ({ key, source: { id } })
    let queue = [entry(0, "a")]
    for (const [key, id] of [[1, "a"], [2, "a"], [3, "b"]] as const) {
      expect(isQueued(queue, id)).toBe(id === "a")
      queue = appendEntry(queue, entry(key, id))
    }
    expect(keys(queue)).toEqual([0, 1, 2, 3])
    expect(isQueued(queue, "c")).toBe(false)
  })

  it("undo removes only the appended entry, after later queue changes", () => {
    let queue = [{ key: 0 }, { key: 1 }]
    queue = appendEntry(queue, { key: 2 })
    queue = appendEntry(queue, { key: 3 })
    queue = moveEntry(queue, 3, -1)
    queue = insertAfter(queue, 0, { key: 4 })
    expect(keys(removeEntry(queue, 2))).toEqual([0, 4, 1, 3])
    expect(keys(removeEntry(removeEntry(queue, 2), 2))).toEqual([0, 4, 1, 3])
    expect(keys(removeEntry(clearExcept(queue, 1), 2))).toEqual([1])
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
