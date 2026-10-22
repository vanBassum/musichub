import {
  isMissingMusicSummary,
  type JobExecution,
  type JobSummary,
  type TaskSummary,
} from "@/lib/jobs-api"

const plural = (count: number, one: string, many: string) =>
  `${count} ${count === 1 ? one : many}`

// The one place that knows what each task's summary says: the headline of a run, and whether it only did
// part of its work.
export function summaryHeadline(summary: TaskSummary) {
  const failures = summaryFailures(summary)
  const failedPart = failures > 0 ? `, ${failures} failed` : ""
  if (isMissingMusicSummary(summary)) {
    return {
      main: `${plural(summary.examined, "track", "tracks")} searched`,
      sub: `${summary.enqueued} queued, ${summary.alreadyQueued} already queued, ${summary.nothingFound} not found${failedPart}`,
    }
  }
  return {
    main: `${plural(summary.examined, "file", "files")} examined`,
    sub: `${summary.linked} linked, ${summary.unresolved} unresolved${failedPart}`,
  }
}

// Only a run that re-identified linked files has these; they are not shown for a default run.
function reIdentifiedFacts(summary: JobSummary): [string, string][] {
  if (summary.retained === 0) return []
  return [["Link kept", plural(summary.retained, "file", "files")]]
}

// How many items a Succeeded run could not process.
export function summaryFailures(summary: TaskSummary | null) {
  if (!summary) return 0
  return isMissingMusicSummary(summary)
    ? summary.failed + summary.providerUnavailable
    : summary.failed
}

// The labelled numbers of the execution details.
export function summaryFacts(summary: TaskSummary): [string, string | number][] {
  if (isMissingMusicSummary(summary)) {
    return [
      ["Searched", plural(summary.examined, "track", "tracks")],
      ["Queued for download", plural(summary.enqueued, "track", "tracks")],
      ["Already queued", plural(summary.alreadyQueued, "track", "tracks")],
      ["Nothing found", plural(summary.nothingFound, "track", "tracks")],
      ["Provider unavailable", plural(summary.providerUnavailable, "track", "tracks")],
      ["Skipped", plural(summary.skipped, "track", "tracks")],
      ["Errors", summary.failed],
    ]
  }
  return [
    ["Attempted", plural(summary.examined, "file", "files")],
    ["Newly linked", plural(summary.linked, "file", "files")],
    ["Tracks created", summary.tracksCreated],
    ...reIdentifiedFacts(summary),
    ["Not identified", plural(summary.unresolved, "file", "files")],
    ["Skipped", plural(summary.skipped, "file", "files")],
    ["Errors", summary.failed],
  ]
}

// The text that changes when a running execution has moved on, so per-execution data is loaded again.
export function executionVersion(execution: JobExecution, revision: number) {
  return `${execution.state}:${execution.summary?.examined ?? ""}:${revision}`
}
