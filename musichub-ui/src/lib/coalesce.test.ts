import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { coalesce } from "./coalesce"

describe("coalesce", () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it("runs the first call at once and collapses a burst into one trailing run", () => {
    const run = vi.fn()
    const call = coalesce(run, 300)

    for (let i = 0; i < 10; i++) call()
    expect(run).toHaveBeenCalledTimes(1)

    vi.advanceTimersByTime(300)
    expect(run).toHaveBeenCalledTimes(2)

    vi.advanceTimersByTime(1000)
    expect(run).toHaveBeenCalledTimes(2)
  })

  it("runs again at once after a quiet window", () => {
    const run = vi.fn()
    const call = coalesce(run, 300)

    call()
    vi.advanceTimersByTime(300)
    call()

    expect(run).toHaveBeenCalledTimes(2)
  })

  it("cancel drops the pending trailing run", () => {
    const run = vi.fn()
    const call = coalesce(run, 300)

    call()
    call()
    call.cancel()
    vi.advanceTimersByTime(1000)

    expect(run).toHaveBeenCalledTimes(1)
  })
})
