import { decisionLabels, downloadLabels, summaryFacts } from "@/lib/job-summary"
import {
  isMissingMusicSummary,
  outcomeSubject,
  type DownloadItem,
  type FileOutcome,
  type JobStateEntry,
  type TaskSummary,
  type TrackAcquisitionOutcome,
  type TrackActivity,
  type TrackActivityKind,
} from "@/lib/jobs-api"

export type RunEventTone = "info" | "success" | "warning" | "error"

// One line of a run's log. `at` is the recorded time; a line without one is a result the data has no time for.
export interface RunEvent {
  at: string | null
  text: string
  tone: RunEventTone
  // Technical text, shown when the line is opened.
  detail?: string
}

type TimedEvent = RunEvent & { at: string }

// What a run recorded, as the log shows it.
export interface RunLogSources {
  history: JobStateEntry[]
  outcomes: FileOutcome[]
  downloads: DownloadItem[]
  activity: TrackActivity[]
  summary: TaskSummary | null
}

// Hangfire's state names, as a log line says them.
const stateLines: Record<string, { text: string; tone: RunEventTone }> = {
  Enqueued: { text: "Queued", tone: "info" },
  Scheduled: { text: "Scheduled to retry", tone: "warning" },
  Processing: { text: "Started", tone: "info" },
  Succeeded: { text: "Completed", tone: "success" },
  Failed: { text: "Failed", tone: "error" },
  Deleted: { text: "Deleted", tone: "info" },
}

const withReason = (text: string, reason: string | null | undefined) =>
  reason ? `${text}: ${reason}` : text

const decisionTones: Partial<Record<FileOutcome["decision"], RunEventTone>> = {
  Identified: "success",
  Error: "error",
  MethodFailed: "error",
  InsufficientEvidence: "warning",
  NoMethodRan: "warning",
  Mismatch: "warning",
}

const downloadTones: Partial<Record<DownloadItem["status"], RunEventTone>> = {
  Imported: "success",
  Failed: "error",
  Rejected: "warning",
}

const activityTones: Partial<Record<TrackActivityKind, RunEventTone>> = {
  DownloadCompleted: "success",
  ImportAccepted: "success",
  NotFound: "warning",
  DownloadRetryScheduled: "warning",
  DownloadInterrupted: "warning",
  ImportRejected: "warning",
  SearchFailed: "error",
  DownloadFailed: "error",
}

const trackTones: Record<TrackAcquisitionOutcome, RunEventTone> = {
  Enqueued: "info",
  AlreadyQueued: "info",
  NothingFound: "warning",
  ProviderUnavailable: "error",
  Skipped: "info",
  Failed: "error",
}

const trackLabels: Record<TrackAcquisitionOutcome, string> = {
  Enqueued: "Queued",
  AlreadyQueued: "Already queued",
  NothingFound: "Nothing found",
  ProviderUnavailable: "Provider unavailable",
  Skipped: "Skipped",
  Failed: "Error",
}

const downloadTitle = (download: DownloadItem) =>
  download.trackTitle ?? `Track ${download.trackId ?? "(deleted)"}`

// The tracks of a Search for Missing Music run, with their results; none for another kind of run.
export const runTracks = (summary: TaskSummary | null) =>
  summary && isMissingMusicSummary(summary) ? summary.tracks : []

function downloadEvents(download: DownloadItem): TimedEvent[] {
  const started: TimedEvent = {
    at: download.startedAt,
    text: `Download started: ${downloadTitle(download)} (${download.provider})`,
    tone: "info",
  }
  if (!download.finishedAt) return [started]
  return [
    started,
    {
      at: download.finishedAt,
      text: withReason(`${downloadTitle(download)}: ${downloadLabels[download.status]}`, download.reason),
      tone: downloadTones[download.status] ?? "info",
    },
  ]
}

// What a Search for Missing Music run concluded for tracks that have no recorded activity of this run (runs from
// before the activity was recorded): the result is known, the time is not.
function untimedTrackEvents(summary: TaskSummary | null, activity: TrackActivity[]): RunEvent[] {
  const recorded = new Set(activity.map((event) => event.trackId))
  return runTracks(summary)
    .filter((result) => !recorded.has(result.trackId))
    .map((result) => ({
      at: null,
      text: withReason(
        `${result.title ? `${result.title} · ${result.artists}` : `Track ${result.trackId}`}: ${trackLabels[result.outcome]}`,
        result.detail
      ),
      tone: trackTones[result.outcome],
    }))
}

// The run's numbers in one line, zero counts left out. It has no time: it sums up the run.
function totalsEvents(summary: TaskSummary | null): RunEvent[] {
  const facts = summary ? summaryFacts(summary).filter((fact) => fact.count !== 0) : []
  if (facts.length === 0) return []
  return [{ at: null, text: `Totals: ${facts.map(({ label, value }) => `${label} ${value}`).join(" · ")}`, tone: "info" }]
}

// A track's name in a log line: the title the run or its downloads know it by.
function trackName(trackId: number, summary: TaskSummary | null, downloads: DownloadItem[]) {
  const title =
    runTracks(summary).find((result) => result.trackId === trackId)?.title ||
    downloads.find((download) => download.trackId === trackId)?.trackTitle
  return title || `Track ${trackId}`
}

// The recorded things that happened in a run, oldest first: its state changes, each track's acquisition events, what
// became of each identified file or candidate, and when each candidate download started and ended. Only what is
// persisted, at the time it was persisted; equal times keep the order they were recorded in. What has no time of its
// own (a track's result from before activity was recorded, the totals) follows at the end, never given a made-up time.
export function buildRunEvents({
  history,
  outcomes,
  downloads,
  activity,
  summary,
}: RunLogSources): RunEvent[] {
  // A download the track activity already tells about (by its id) is not told a second time.
  const told = new Set(activity.map((event) => event.acquisitionDownloadId))
  const timed: TimedEvent[] = [
    ...history.map((entry) => {
      const line = stateLines[entry.state] ?? { text: entry.state, tone: "info" as const }
      return { at: entry.at, text: withReason(line.text, entry.reason), tone: line.tone }
    }),
    ...activity.map((event) => ({
      at: event.occurredAt,
      text: `${trackName(event.trackId, summary, downloads)}: ${event.summary}`,
      tone: activityTones[event.kind] ?? "info",
      detail: event.detail ?? undefined,
    })),
    ...downloads.filter((download) => !told.has(download.id)).flatMap(downloadEvents),
    ...outcomes.map((outcome) => ({
      at: outcome.recordedAt,
      text: withReason(
        `${outcome.fileName || outcomeSubject(outcome)}: ${decisionLabels[outcome.decision]}`,
        outcome.reason
      ),
      tone: decisionTones[outcome.decision] ?? "info",
    })),
  ]
  // Array.prototype.sort is stable, so equal times keep their order.
  timed.sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime())
  return [...timed, ...untimedTrackEvents(summary, activity), ...totalsEvents(summary)]
}
