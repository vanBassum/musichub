import { describe, expect, it } from "vitest"

import type {
  DownloadItem,
  FileOutcome,
  MissingMusicSummary,
  TrackActivity,
} from "@/lib/jobs-api"
import { buildRunEvents, type RunLogSources } from "@/lib/run-events"

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

const activity = (over: Partial<TrackActivity>): TrackActivity => ({
  id: 1,
  trackId: 3,
  kind: "SearchCompleted",
  occurredAt: "2026-10-23T10:00:03Z",
  summary: "Search finished: 5 results.",
  detail: null,
  jobExecutionId: "1",
  acquisitionDownloadId: null,
  attemptNumber: null,
  resultCount: 5,
  plausibleCount: 2,
  eligibleCount: 1,
  ...over,
})

const missingMusic = (): MissingMusicSummary => ({
  examined: 2,
  enqueued: 1,
  alreadyQueued: 0,
  nothingFound: 1,
  providerUnavailable: 0,
  skipped: 0,
  failed: 0,
  tracks: [
    { trackId: 3, title: "Song", artists: "Band", outcome: "Enqueued", detail: "queued" },
    { trackId: 4, title: "Other", artists: "Band", outcome: "NothingFound", detail: "no mp3" },
  ],
})

const sources = (over: Partial<RunLogSources>): RunLogSources => ({
  history: [],
  outcomes: [],
  downloads: [],
  activity: [],
  summary: null,
  ...over,
})

describe("buildRunEvents", () => {
  it("merges state changes, downloads and outcomes oldest first", () => {
    const events = buildRunEvents(
      sources({
        history: [
          { state: "Enqueued", at: "2026-10-23T10:00:00Z", reason: null },
          { state: "Processing", at: "2026-10-23T10:00:01Z", reason: null },
          { state: "Succeeded", at: "2026-10-23T10:00:09Z", reason: null },
        ],
        outcomes: [outcome({})],
        downloads: [download({})],
      })
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
      sources({
        history: [{ state: "Awaiting", at: "2026-10-23T10:00:00Z", reason: "slot" }],
        downloads: [download({ finishedAt: null, status: "Downloading" })],
      })
    )
    expect(events.map((e) => e.text)).toEqual([
      "Awaiting: slot",
      "Download started: Song (slskd)",
    ])
  })

  it("is empty when nothing is recorded", () => {
    expect(buildRunEvents(sources({}))).toEqual([])
  })

  it("puts recorded track activity in time order with its technical detail", () => {
    const events = buildRunEvents(
      sources({
        history: [{ state: "Processing", at: "2026-10-23T10:00:01Z", reason: null }],
        summary: missingMusic(),
        activity: [
          activity({
            id: 2,
            kind: "DownloadFailed",
            occurredAt: "2026-10-23T10:00:08Z",
            summary: "Download failed.",
            detail: "timed out",
          }),
          activity({}),
        ],
      })
    )
    expect(events.slice(0, 3).map((e) => [e.at, e.text])).toEqual([
      ["2026-10-23T10:00:01Z", "Started"],
      ["2026-10-23T10:00:03Z", "Song: Search finished: 5 results."],
      ["2026-10-23T10:00:08Z", "Song: Download failed."],
    ])
    expect(events[2]).toMatchObject({ tone: "error", detail: "timed out" })
  })

  it("lists a track without recorded activity untimed after the timed lines, never giving it a time", () => {
    const events = buildRunEvents(
      sources({
        history: [{ state: "Succeeded", at: "2026-10-23T10:00:09Z", reason: null }],
        summary: missingMusic(),
        activity: [activity({})],
      })
    )
    expect(events.map((e) => [e.at, e.text])).toEqual([
      ["2026-10-23T10:00:03Z", "Song: Search finished: 5 results."],
      ["2026-10-23T10:00:09Z", "Completed"],
      [null, "Other · Band: Nothing found: no mp3"],
      [null, "Totals: Searched 2 tracks · Queued for download 1 track · Nothing found 1 track"],
    ])
  })

  it("does not tell a download twice when the activity already tells about it", () => {
    const events = buildRunEvents(
      sources({
        downloads: [download({ id: 9 })],
        activity: [
          activity({ kind: "DownloadStarted", acquisitionDownloadId: 9, summary: "Download started." }),
        ],
      })
    )
    expect(events.map((e) => e.text)).toEqual(["Song: Download started."])
  })
})
