import { ArrowLeft, ChevronRight } from "lucide-react"
import type { ReactNode } from "react"

import { IdentificationOutcomes } from "@/components/identification-outcomes"
import { MissingMusicResults } from "@/components/missing-music-results"
import { elapsed, StatusBadge, when } from "@/components/run-status"
import { Button } from "@/components/ui/button"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import { Skeleton } from "@/components/ui/skeleton"
import {
  executionVersion,
  summaryFacts,
} from "@/lib/job-summary"
import type { JobExecutionDetails } from "@/lib/jobs-api"
import { buildRunEvents, type RunEventTone } from "@/lib/run-events"
import type { JobsState } from "@/lib/use-jobs"
import { useDownloads } from "@/lib/use-downloads"
import { useOutcomes } from "@/lib/use-outcomes"
import { cn } from "@/lib/utils"

const toneClassNames: Record<RunEventTone, string> = {
  info: "",
  success: "text-success",
  warning: "text-warning",
  error: "text-destructive",
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right">{children}</dd>
    </div>
  )
}

// The chronological log of one run: its state changes and, as they are recorded, what became of each file or
// candidate. The state changes show at once; the rest joins when it has loaded.
function RunLog({ details, version }: { details: JobExecutionDetails; version: string }) {
  const { execution, history } = details
  const { outcomes, error: outcomesError } = useOutcomes(execution.id, version)
  const { downloads, error: downloadsError } = useDownloads(execution.id, version)
  const events = buildRunEvents(history, outcomes ?? [], downloads ?? [])
  const loading = (outcomes === null && !outcomesError) || (downloads === null && !downloadsError)

  return (
    <div className="flex flex-col gap-2">
      <h3 className="text-sm font-semibold">Log</h3>
      {outcomesError && (
        <p role="alert" className="text-sm text-destructive">
          {outcomesError}
        </p>
      )}
      {downloadsError && (
        <p role="alert" className="text-sm text-destructive">
          {downloadsError}
        </p>
      )}
      {events.length === 0 ? (
        <p className="text-sm text-muted-foreground">No events are recorded for this run.</p>
      ) : (
        <ol
          aria-label="Run log"
          // Focusable, so a long log scrolls with the keyboard.
          tabIndex={0}
          className="flex max-h-[28rem] flex-col gap-1 overflow-auto rounded-lg border bg-muted/40 p-3 font-mono text-xs"
        >
          {events.map((event, index) => (
            <li key={index} className="flex gap-3">
              <time dateTime={event.at} className="shrink-0 text-muted-foreground">
                {new Date(event.at).toLocaleTimeString()}
              </time>
              <span className={cn("min-w-0 break-words", toneClassNames[event.tone])}>
                {event.text}
              </span>
            </li>
          ))}
        </ol>
      )}
      {loading && <p className="text-xs text-muted-foreground">Loading more events…</p>}
    </div>
  )
}

// What the run did in numbers and per item, for when the log is not enough.
function TechnicalDetails({
  details,
  version,
}: {
  details: JobExecutionDetails
  version: string
}) {
  const { execution } = details
  return (
    <Collapsible className="group">
      <CollapsibleTrigger className="flex items-center gap-1 text-sm font-semibold">
        <ChevronRight
          aria-hidden="true"
          className="size-4 transition-transform group-data-[open]:rotate-90"
        />
        Technical details
      </CollapsibleTrigger>
      <CollapsibleContent className="mt-3 flex flex-col gap-4">
        {execution.summary && (
          <dl className="flex flex-col gap-2 text-sm">
            {summaryFacts(execution.summary).map(([label, value]) => (
              <Fact key={label} label={label}>
                {value}
              </Fact>
            ))}
          </dl>
        )}
        {execution.kind === "missing-music-acquisition" && (
          <MissingMusicResults execution={execution} version={version} />
        )}
        <IdentificationOutcomes executionId={execution.id} version={version} />
      </CollapsibleContent>
    </Collapsible>
  )
}

function DetailsPlaceholder({
  selectedId,
  detailsError,
}: Pick<JobsState, "selectedId" | "detailsError">) {
  if (selectedId === null)
    return <p className="text-muted-foreground">Select a run to see its log.</p>
  if (detailsError)
    return (
      <p role="alert" className="text-destructive">
        {detailsError}
      </p>
    )
  return <Skeleton className="h-32 w-full" />
}

// The selected run: which job, how it went and its log. On a narrow screen it replaces the lists.
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
          <TechnicalDetails details={details} version={version} />
        </>
      )}
    </section>
  )
}
