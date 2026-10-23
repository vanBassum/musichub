import { decisionLabels, downloadLabels } from "@/lib/job-summary"
import {
  outcomeSubject,
  type DownloadItem,
  type FileOutcome,
  type JobStateEntry,
} from "@/lib/jobs-api"

export type RunEventTone = "info" | "success" | "warning" | "error"

// One timestamped line of a run's log.
export interface RunEvent {
  at: string
  text: string
  tone: RunEventTone
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

const decisionTone = (outcome: FileOutcome): RunEventTone => {
  switch (outcome.decision) {
    case "Identified":
      return "success"
    case "Error":
    case "MethodFailed":
      return "error"
    case "InsufficientEvidence":
    case "NoMethodRan":
    case "Mismatch":
      return "warning"
    default:
      return "info"
  }
}

const downloadTone = (download: DownloadItem): RunEventTone => {
  switch (download.status) {
    case "Imported":
      return "success"
    case "Failed":
      return "error"
    case "Rejected":
      return "warning"
    default:
      return "info"
  }
}

const downloadTitle = (download: DownloadItem) =>
  download.trackTitle ?? `Track ${download.trackId ?? "(deleted)"}`

// The recorded things that happened in a run, oldest first: its state changes, what became of each identified file
// or candidate, and when each candidate download started and ended. Only what is persisted; nothing is made up.
// Events with the same time keep the order they were recorded in.
export function buildRunEvents(
  history: JobStateEntry[],
  outcomes: FileOutcome[],
  downloads: DownloadItem[]
): RunEvent[] {
  const events: RunEvent[] = [
    ...history.map((entry) => {
      const line = stateLines[entry.state] ?? { text: entry.state, tone: "info" as const }
      return { at: entry.at, text: withReason(line.text, entry.reason), tone: line.tone }
    }),
    ...downloads.flatMap((download) => {
      const started: RunEvent = {
        at: download.startedAt,
        text: `Download started: ${downloadTitle(download)} (${download.provider})`,
        tone: "info",
      }
      if (!download.finishedAt) return [started]
      return [
        started,
        {
          at: download.finishedAt,
          text: withReason(
            `${downloadTitle(download)}: ${downloadLabels[download.status]}`,
            download.reason
          ),
          tone: downloadTone(download),
        },
      ]
    }),
    ...outcomes.map((outcome) => ({
      at: outcome.recordedAt,
      text: withReason(
        `${outcome.fileName || outcomeSubject(outcome)}: ${decisionLabels[outcome.decision]}`,
        outcome.reason
      ),
      tone: decisionTone(outcome),
    })),
  ]
  // Array.prototype.sort is stable, so equal times keep their order.
  return events.sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime())
}
