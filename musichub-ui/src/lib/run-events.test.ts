import { describe, expect, it } from "vitest"

import { chronological, stateEvents, type RunEvent } from "@/lib/run-events"

const event = (key: string, at: string): RunEvent => ({
  key,
  at,
  text: key,
  tone: "neutral",
})

describe("chronological", () => {
  it("orders oldest first and keeps equal instants in the given order", () => {
    const sorted = chronological([
      event("late", "2026-10-23T10:00:05Z"),
      event("first-tie", "2026-10-23T10:00:01Z"),
      event("second-tie", "2026-10-23T10:00:01Z"),
    ])
    expect(sorted.map((e) => e.key)).toEqual(["first-tie", "second-tie", "late"])
  })
})

describe("stateEvents", () => {
  it("shows the reason and marks a failure", () => {
    const events = stateEvents([
      { state: "Enqueued", at: "2026-10-23T10:00:00Z", reason: null },
      { state: "Failed", at: "2026-10-23T10:00:02Z", reason: "boom" },
    ])
    expect(events.map((e) => e.text)).toEqual(["Enqueued", "Failed: boom"])
    expect(events.map((e) => e.tone)).toEqual(["neutral", "error"])
  })
})
