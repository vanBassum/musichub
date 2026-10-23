import { useMemo, useState } from "react"

import { decisionLabels } from "@/components/identification-outcomes"
import { downloadLabels } from "@/components/missing-music-results"
import { Button } from "@/components/ui/button"
import {
  missingMusicTaskId,
  outcomeSubject,
  type DownloadItem,
  type DownloadStatus,
  type FileOutcome,
  type IdentificationDecision,
  type JobExecutionDetails,
} from "@/lib/jobs-api"
import {
  chronological,
  stateEvents,
  type EventTone,
  type RunEvent,
} from "@/lib/run-events"
import { useDownloads } from "@/lib/use-downloads"
import { useOutcomes } from "@/lib/use-outcomes"
import { cn } from "@/lib/utils"

// A long run records a line per file; only this many show until the reader asks for the rest.
const initialLines = 200

const decisionTones: Record<IdentificationDecision, EventTone> = {
  Identified: "success",
  NoMatch: "neutral",
  InsufficientEvidence: "warning",
  NoMethodRan: "warning",
  MethodFailed: "error",
  Skipped: "neutral",
  Error: "error",
  Mismatch: "warning",
}

const downloadTones: Record<DownloadStatus, EventTone> = {
  Downloading: "neutral",
  Failed: "error",
  Cancelled: "neutral",
  Rejected: "warning",
  Imported: "success",
}

const toneClassNames: Record<EventTone, string> = {
  neutral: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  error: "text-destructive",
}

const withReason = (text: string, reason: string) =>
  reason ? `${text} – ${reason}` : text

function outcomeEvents(outcomes: FileOutcome[]): RunEvent[] {
  return outcomes.map((outcome) => ({
    key: `outcome-${outcomeSubject(outcome)}-${outcome.recordedAt}`,
    at: outcome.recordedAt,
    text: withReason(
      `${decisionLabels[outcome.decision]}: ${outcome.fileName || outcomeSubject(outcome)}`,
      outcome.reason
    ),
    tone: decisionTones[outcome.decision],
  }))
}

function downloadEvents(downloads: DownloadItem[]): RunEvent[] {
  return downloads.flatMap((download) => {
    const title = download.trackTitle ?? `track ${download.trackId ?? "(deleted)"}`
    const started: RunEvent = {
      key: `download-${download.id}-start`,
      at: download.startedAt,
      text: `Download started: ${title} from ${download.source}`,
      tone: "neutral",
    }
    if (!download.finishedAt) return [started]
    return [
      started,
      {
        key: `download-${download.id}-end`,
        at: download.finishedAt,
        text: withReason(`${downloadLabels[download.status]}: ${title}`, download.reason),
        tone: downloadTones[download.status],
      },
    ]
  })
}

// The recorded lines of one run, oldest first: its state changes, what it decided about each file and, for a search
// for missing music, each candidate it downloaded. Only what was persisted with a time shows; nothing is made up.
export function RunLog({
  details,
  version,
}: {
  details: JobExecutionDetails
  version: string
}) {
  const { execution, history } = details
  const { outcomes, error: outcomesError } = useOutcomes(execution.id, version)
  const { downloads, error: downloadsError } = useDownloads(
    execution.id,
    version,
    execution.kind === missingMusicTaskId
  )
  const [showAll, setShowAll] = useState(false)

  const events = useMemo(
    () =>
      chronological([
        ...stateEvents(history),
        ...outcomeEvents(outcomes ?? []),
        ...downloadEvents(downloads ?? []),
      ]),
    [history, outcomes, downloads]
  )
  const error = outcomesError ?? downloadsError
  const loading = !error && (outcomes === null || downloads === null)
  const shown = showAll ? events : events.slice(0, initialLines)

  return (
    <div>
      <h3 className="mb-2 text-sm font-semibold">Log</h3>
      {error && (
        <p role="alert" className="mb-2 text-sm text-destructive">
          {error}
        </p>
      )}
      <ol
        aria-label="Run log"
        className="flex flex-col gap-0.5 rounded-lg border bg-muted/40 p-2 font-mono text-xs"
      >
        {shown.map((event) => (
          <li key={event.key} className="flex gap-2">
            <time dateTime={event.at} className="shrink-0 text-muted-foreground">
              {new Date(event.at).toLocaleTimeString()}
            </time>
            <span className={cn("min-w-0 break-words", toneClassNames[event.tone])}>
              {event.text}
            </span>
          </li>
        ))}
      </ol>
      {loading && (
        <p role="status" className="mt-2 text-xs text-muted-foreground">
          Loading more events…
        </p>
      )}
      {events.length > shown.length && (
        <Button
          variant="outline"
          size="sm"
          className="mt-2"
          onClick={() => setShowAll(true)}
        >
          Show all {events.length} lines
        </Button>
      )}
    </div>
  )
}
