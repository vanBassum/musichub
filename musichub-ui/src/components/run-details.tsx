import { ArrowLeft, ChevronRight } from "lucide-react"

import { elapsed, StatusBadge, when } from "@/components/run-status"
import { Button } from "@/components/ui/button"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import { Skeleton } from "@/components/ui/skeleton"
import { executionVersion } from "@/lib/job-summary"
import type { JobExecutionDetails } from "@/lib/jobs-api"
import { buildRunEvents, runTracks, type RunEvent, type RunEventTone } from "@/lib/run-events"
import type { JobsState } from "@/lib/use-jobs"
import { useDownloads } from "@/lib/use-downloads"
import { useOutcomes } from "@/lib/use-outcomes"
import { useTrackActivity } from "@/lib/use-track-activity"
import { cn } from "@/lib/utils"

const toneClassNames: Record<RunEventTone, string> = {
  info: "",
  success: "text-success",
  warning: "text-warning",
  error: "text-destructive",
}

// One line of the log; a line with technical text opens to show it.
function LogLine({ event }: { event: RunEvent }) {
  const text = (
    <span className={cn("min-w-0 break-words", toneClassNames[event.tone])}>{event.text}</span>
  )
  return (
    <li className="flex gap-3">
      {event.at ? (
        <time dateTime={event.at} className="shrink-0 text-muted-foreground">
          {new Date(event.at).toLocaleTimeString()}
        </time>
      ) : (
        <span className="shrink-0 text-muted-foreground" title="No time was recorded">
          --:--:--
        </span>
      )}
      {event.detail ? (
        <Collapsible className="group min-w-0">
          <CollapsibleTrigger className="flex items-start gap-1 text-left">
            <ChevronRight
              aria-hidden="true"
              className="mt-0.5 size-3 shrink-0 transition-transform group-data-[open]:rotate-90"
            />
            {text}
          </CollapsibleTrigger>
          <CollapsibleContent className="mt-1 break-words text-muted-foreground">
            {event.detail}
          </CollapsibleContent>
        </Collapsible>
      ) : (
        text
      )}
    </li>
  )
}

// The one chronological log of a run: its state changes, each track's recorded acquisition events, what became of
// each file or candidate, and the run's totals. The state changes show at once; the rest joins when it has loaded.
// What the data has no time for follows the timed lines, marked as untimed.
function RunLog({ details, version }: { details: JobExecutionDetails; version: string }) {
  const { execution, history } = details
  const { outcomes, error: outcomesError } = useOutcomes(execution.id, version)
  const { downloads, error: downloadsError } = useDownloads(execution.id, version)
  const trackIds = runTracks(execution.summary).map((result) => result.trackId)
  const { activity, error: activityError } = useTrackActivity(execution.id, trackIds, version)
  const events = buildRunEvents({
    history,
    outcomes: outcomes ?? [],
    downloads: downloads ?? [],
    activity: activity ?? [],
    summary: execution.summary,
  })
  const errors = [outcomesError, downloadsError, activityError].filter(Boolean)
  const loading =
    (outcomes === null && !outcomesError) ||
    (downloads === null && !downloadsError) ||
    (activity === null && !activityError)

  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-sm font-semibold">Activity</h3>
      {errors.map((message) => (
        <p key={message} role="alert" className="text-sm text-destructive">
          {message}
        </p>
      ))}
      <ol
        aria-label="Run activity"
        // Focusable, so a long log scrolls with the keyboard.
        tabIndex={0}
        className="flex max-h-[32rem] flex-col gap-1 overflow-auto rounded-lg border bg-muted/40 p-3 font-mono text-xs"
      >
        {events.map((event, index) => (
          <LogLine key={index} event={event} />
        ))}
      </ol>
      {events.some((event) => event.at === null) && (
        <p className="text-xs text-muted-foreground">
          Lines marked --:--:-- have no recorded time, so their place in the order is not known.
        </p>
      )}
      {loading && <p className="text-xs text-muted-foreground">Loading more events…</p>}
    </div>
  )
}

function DetailsPlaceholder({
  selectedId,
  detailsError,
}: Pick<JobsState, "selectedId" | "detailsError">) {
  if (selectedId === null)
    return <p className="text-muted-foreground">Select a run to see its activity.</p>
  if (detailsError)
    return (
      <p role="alert" className="text-destructive">
        {detailsError}
      </p>
    )
  return <Skeleton className="h-32 w-full" />
}

// The selected run: which job, how it went and its activity log. On a narrow screen it replaces the lists.
export function RunDetails({
  state,
  jobName,
  className,
}: {
  state: JobsState
  // The title of a run's job, from its kind.
  jobName: (kind: string) => string
  className?: string
}) {
  const { details, detailsRevision, detailsError, selectedId, closeDetails } = state
  const version = details ? executionVersion(details.execution, detailsRevision) : ""
  return (
    <section
      aria-labelledby="details-heading"
      className={cn("flex min-w-0 flex-col gap-4 rounded-xl border bg-card p-4", className)}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 items-start gap-2">
          <Button
            variant="outline"
            size="icon"
            aria-label="Back to runs"
            className="lg:hidden"
            onClick={closeDetails}
          >
            <ArrowLeft />
          </Button>
          <div className="min-w-0">
            <h2 id="details-heading" className="text-lg font-semibold break-words">
              {details ? jobName(details.execution.kind) : "Run details"}
            </h2>
            {details && (
              <p className="text-sm text-muted-foreground">
                {when(details.execution.startedAt ?? details.execution.createdAt)} ·{" "}
                {elapsed(details.execution)}
              </p>
            )}
          </div>
        </div>
        {details && <StatusBadge execution={details.execution} />}
      </div>
      {!details ? (
        <DetailsPlaceholder selectedId={selectedId} detailsError={detailsError} />
      ) : (
        <>
          {details.execution.error && (
            <p role="alert" className="break-words text-destructive">
              {details.errorType ? `${details.errorType}: ` : ""}
              {details.execution.error}
            </p>
          )}
          <RunLog details={details} version={version} />
        </>
      )}
    </section>
  )
}
