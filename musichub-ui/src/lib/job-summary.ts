import {
  isMissingMusicSummary,
  type DownloadStatus,
  type IdentificationDecision,
  type JobExecution,
  type JobSummary,
  type TaskSummary,
} from "@/lib/jobs-api"

export const decisionLabels: Record<IdentificationDecision, string> = {
  Identified: "Linked",
  NoMatch: "No match",
  InsufficientEvidence: "Not enough evidence",
  NoMethodRan: "No method could run",
  MethodFailed: "A method failed",
  Skipped: "Skipped",
  Error: "Error",
  Mismatch: "Another recording",
}

export const downloadLabels: Record<DownloadStatus, string> = {
  Downloading: "Downloading",
  Failed: "Download failed",
  Cancelled: "Cancelled",
  Rejected: "Rejected",
  Imported: "Imported",
}

export type SummaryFact = { label: string; value: string | number; count: number }

const plural = (count: number, one: string, many: string) =>
  `${count} ${count === 1 ? one : many}`

// A labelled number; `count` is the raw number, `value` what is shown (with its unit when one is given).
const countFact = (label: string, count: number, one?: string, many?: string): SummaryFact => ({
  label,
  value: one && many ? plural(count, one, many) : count,
  count,
})

// Only a run that re-identified linked files has these; they are not shown for a default run.
function reIdentifiedFacts(summary: JobSummary): SummaryFact[] {
  if (summary.retained === 0) return []
  return [countFact("Link kept", summary.retained, "file", "files")]
}

// How many items a Succeeded run could not process.
export function summaryFailures(summary: TaskSummary | null) {
  if (!summary) return 0
  return isMissingMusicSummary(summary)
    ? summary.failed + summary.providerUnavailable
    : summary.failed
}

// The labelled numbers of the execution details.
export function summaryFacts(summary: TaskSummary): SummaryFact[] {
  if (isMissingMusicSummary(summary)) {
    return [
      countFact("Searched", summary.examined, "track", "tracks"),
      countFact("Queued for download", summary.enqueued, "track", "tracks"),
      countFact("Already queued", summary.alreadyQueued, "track", "tracks"),
      countFact("Nothing found", summary.nothingFound, "track", "tracks"),
      countFact("Provider unavailable", summary.providerUnavailable, "track", "tracks"),
      countFact("Skipped", summary.skipped, "track", "tracks"),
      countFact("Errors", summary.failed),
    ]
  }
  return [
    countFact("Attempted", summary.examined, "file", "files"),
    countFact("Newly linked", summary.linked, "file", "files"),
    countFact("Tracks created", summary.tracksCreated),
    ...reIdentifiedFacts(summary),
    countFact("Not identified", summary.unresolved, "file", "files"),
    countFact("Skipped", summary.skipped, "file", "files"),
    countFact("Errors", summary.failed),
  ]
}

// The text that changes when a running execution has moved on, so per-execution data is loaded again.
export function executionVersion(execution: JobExecution, revision: number) {
  return `${execution.state}:${execution.summary?.examined ?? ""}:${revision}`
}
