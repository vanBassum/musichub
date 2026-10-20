import {
  isMissingMusicSummary,
  type JobExecution,
  type TaskSummary,
} from "@/lib/jobs-api"

const plural = (count: number, one: string, many: string) =>
  `${count} ${count === 1 ? one : many}`

// The one place that knows what each task's summary says: the headline of a run, and whether it only did
// part of its work.
export function summaryHeadline(summary: TaskSummary) {
  if (isMissingMusicSummary(summary)) {
    return {
      main: `${plural(summary.examined, "track", "tracks")} searched`,
      sub: `${summary.imported} imported, ${summary.nothingFound + summary.noAcceptableCandidate} not found${
        summary.failed + summary.providerUnavailable > 0
          ? `, ${summary.failed + summary.providerUnavailable} failed`
          : ""
      }`,
    }
  }
  return {
    main: `${plural(summary.examined, "file", "files")} examined`,
    sub: `${summary.linked} linked, ${summary.unresolved} unresolved${
      summary.failed > 0 ? `, ${summary.failed} failed` : ""
    }`,
  }
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
      ["Imported", plural(summary.imported, "track", "tracks")],
      ["Nothing found", plural(summary.nothingFound, "track", "tracks")],
      ["None accepted", plural(summary.noAcceptableCandidate, "track", "tracks")],
      ["Provider unavailable", plural(summary.providerUnavailable, "track", "tracks")],
      ["Skipped", plural(summary.skipped, "track", "tracks")],
      ["Errors", summary.failed],
    ]
  }
  return [
    ["Examined", plural(summary.examined, "file", "files")],
    ["Linked", plural(summary.linked, "file", "files")],
    ["Tracks created", summary.tracksCreated],
    ["Not identified", plural(summary.unresolved, "file", "files")],
    ["Skipped", plural(summary.skipped, "file", "files")],
    ["Errors", summary.failed],
  ]
}

// The text that changes when a running execution has moved on, so per-execution data is loaded again.
export function executionVersion(execution: JobExecution, revision: number) {
  return `${execution.state}:${execution.summary?.examined ?? ""}:${revision}`
}
