import { describe, expect, it } from "vitest"

import type { DownloadItem, FileOutcome } from "@/lib/jobs-api"
import { buildRunEvents } from "@/lib/run-events"

const outcome = (over: Partial<FileOutcome>): FileOutcome => ({
  jobExecutionId: "1",
  fileId: 7,
  downloadId: null,
  fileName: "a.flac",
  decision: "Identified",
  reason: "matched",
  trackId: 1,
  recordedAt: "2026-10-23T10:00:05Z",
  methods: [],
  pipelineVersion: null,
  ...over,
})

const download = (over: Partial<DownloadItem>): DownloadItem => ({
  id: 1,
  trackId: 3,
  trackTitle: "Song",
  provider: "slskd",
  source: "user",
  sourcePath: "x",
  sizeBytes: 1,
  bitrateKbps: null,
  status: "Failed",
  reason: "timed out",
  fileSha256: null,
  audioHash: null,
  audioHashStatus: null,
  fileId: null,
  startedAt: "2026-10-23T10:00:02Z",
  finishedAt: "2026-10-23T10:00:04Z",
  ...over,
})

describe("buildRunEvents", () => {
  it("merges state changes, downloads and outcomes oldest first", () => {
    const events = buildRunEvents(
      [
        { state: "Enqueued", at: "2026-10-23T10:00:00Z", reason: null },
        { state: "Processing", at: "2026-10-23T10:00:01Z", reason: null },
        { state: "Succeeded", at: "2026-10-23T10:00:09Z", reason: null },
      ],
      [outcome({})],
      [download({})]
    )
    expect(events.map((e) => e.text)).toEqual([
      "Queued",
      "Started",
      "Download started: Song (slskd)",
      "Song: Download failed: timed out",
      "a.flac: Linked: matched",
      "Completed",
    ])
    expect(events[3].tone).toBe("error")
  })

  it("shows a running download as started only and names an unknown state as it is", () => {
    const events = buildRunEvents(
      [{ state: "Awaiting", at: "2026-10-23T10:00:00Z", reason: "slot" }],
      [],
      [download({ finishedAt: null, status: "Downloading" })]
    )
    expect(events.map((e) => e.text)).toEqual([
      "Awaiting: slot",
      "Download started: Song (slskd)",
    ])
  })

  it("is empty when nothing is recorded", () => {
    expect(buildRunEvents([], [], [])).toEqual([])
  })
})
