import { Database, Play, RefreshCw } from "lucide-react"
import type { ReactNode } from "react"

import { IdentificationHistory } from "@/components/identification-history"
import { IdentificationOutcomes } from "@/components/identification-outcomes"
import { MissingMusicResults } from "@/components/missing-music-results"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import type { JobExecution, JobSchedule, JobTask } from "@/lib/jobs-api"
import { formatElapsed } from "@/lib/format"
import {
  executionVersion,
  summaryFacts,
  summaryFailures,
  summaryHeadline,
} from "@/lib/job-summary"
import type { JobsState } from "@/lib/use-jobs"
import { cn } from "@/lib/utils"

type Outcome = "waiting" | "retrying" | "running" | "completed" | "partial" | "failed" | "deleted"

const outcomeLabels: Record<Outcome, string> = {
  waiting: "Waiting",
  retrying: "Retrying",
  running: "Running",
  completed: "Completed",
  partial: "Partial",
  failed: "Failed",
  deleted: "Deleted",
}

const outcomeClassNames: Record<Outcome, string> = {
  waiting: "bg-muted text-muted-foreground",
  retrying: "bg-warning text-warning-foreground",
  running: "bg-primary text-primary-foreground",
  completed: "bg-success text-success-foreground",
  partial: "bg-warning text-warning-foreground",
  failed: "bg-destructive text-white",
  deleted: "bg-muted text-muted-foreground",
}

// A run that Succeeded but could not process some files is shown as Partial.
function outcomeOf(execution: JobExecution): Outcome {
  switch (execution.state) {
    case "Enqueued":
      return "waiting"
    case "Scheduled":
      return "retrying"
    case "Processing":
      return "running"
    case "Succeeded":
      return summaryFailures(execution.summary) > 0 ? "partial" : "completed"
    case "Failed":
      return "failed"
    default:
      return "deleted"
  }
}

function describeResult(execution: JobExecution) {
  const { summary, error } = execution
  if (summary) return summaryHeadline(summary)
  if (error) return { main: "Stopped after an error", sub: error }
  const state = outcomeLabels[outcomeOf(execution)]
  return { main: state, sub: null }
}

function elapsed(execution: JobExecution) {
  if (!execution.startedAt) return "–"
  const end = execution.finishedAt ? new Date(execution.finishedAt) : new Date()
  return formatElapsed(end.getTime() - new Date(execution.startedAt).getTime())
}

function when(iso: string | null) {
  return iso ? new Date(iso).toLocaleString() : "–"
}

function StatusBadge({ execution }: { execution: JobExecution }) {
  const outcome = outcomeOf(execution)
  return (
    <Badge className={outcomeClassNames[outcome]}>{outcomeLabels[outcome]}</Badge>
  )
}

// When a task runs next, or why it has no schedule. Nothing for a task that cannot be scheduled.
function ScheduleLine({ schedule }: { schedule: JobSchedule | null }) {
  if (!schedule) return null
  if (schedule.problem)
    return (
      <p role="alert" className="text-sm break-words text-destructive">
        {schedule.problem}
      </p>
    )
  return (
    <p className="text-sm text-muted-foreground">
      {schedule.enabled
        ? `Next run ${when(schedule.nextRun)} · cron ${schedule.cron} (UTC)`
        : "Not scheduled, starts by hand only"}
    </p>
  )
}

// A task and its start button. One start is in flight at a time, and a task refuses a second run while one is active.
function TaskCard({
  task,
  starting,
  start,
}: {
  task: JobTask
  starting: JobsState["starting"]
  start: JobsState["start"]
}) {
  const running = starting === task.id || task.activeExecutionId !== null
  return (
    <li className="flex items-start gap-3 rounded-xl border border-primary bg-card p-4">
      <span className="rounded-lg bg-primary/10 p-2 text-primary">
        <Database aria-hidden="true" />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-2">
        <div>
          <h2 className="font-semibold">{task.name}</h2>
          <p className="text-sm text-muted-foreground">{task.description}</p>
        </div>
        <ScheduleLine schedule={task.schedule} />
        <div className="flex flex-wrap items-center justify-between gap-2">
          <p className="flex items-center gap-2 text-sm">
            <span
              className={cn("size-2 rounded-full", running ? "bg-primary" : "bg-success")}
            />
            {running ? "Running" : "Available"}
          </p>
          <Button
            onClick={() => start(task.id)}
            disabled={running || starting !== null}
          >
            <Play data-icon="inline-start" />
            {running ? "Running…" : "Start"}
          </Button>
        </div>
      </div>
    </li>
  )
}

export function JobsView({
  state,
  className,
}: {
  state: JobsState
  className?: string
}) {
  const {
    tasks,
    executions,
    loadError,
    refreshing,
    selectedId,
    select,
    starting,
    startError,
    refresh,
    start,
  } = state

  return (
    <section
      aria-labelledby="jobs-heading"
      className={cn("flex min-h-0 min-w-0 flex-col gap-4 overflow-auto", className)}
    >
      <div className="flex shrink-0 flex-wrap items-center gap-3">
        <div>
          <h1 id="jobs-heading" className="text-xl font-semibold">
            Background tasks
          </h1>
          <p className="text-muted-foreground">
            View and start the background tasks of MusicHub
          </p>
        </div>
        <div className="ml-auto flex items-center gap-2">
          <Button
            variant="outline"
            size="lg"
            onClick={() => void refresh()}
            disabled={refreshing}
          >
            <RefreshCw data-icon="inline-start" />
            Refresh
          </Button>
        </div>
      </div>

      {startError && (
        <p role="alert" className="shrink-0 text-sm text-destructive">
          {startError}
        </p>
      )}
      {loadError && (
        <p role="alert" className="shrink-0 text-sm text-destructive">
          {loadError}
        </p>
      )}

      <div className="shrink-0">
        {tasks === null ? (
          <Skeleton className="h-28 w-full max-w-xl rounded-xl" />
        ) : (
          <ul aria-label="Tasks" className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {tasks.map((item) => (
              <TaskCard key={item.id} task={item} starting={starting} start={start} />
            ))}
          </ul>
        )}
      </div>

      <div className="grid min-h-0 flex-1 gap-4 lg:grid-cols-[minmax(0,1fr)_24rem]">
        <section
          aria-labelledby="executions-heading"
          className="flex min-h-64 min-w-0 flex-col overflow-hidden rounded-xl border bg-card"
        >
          <h2 id="executions-heading" className="shrink-0 p-4 text-lg font-semibold">
            Recent executions
          </h2>
          <div className="min-h-0 flex-1 overflow-auto border-t [&_[data-slot=table-container]]:overflow-visible">
            <Table className="table-fixed text-sm sm:table-auto">
              <TableHeader className="sticky top-0 z-10 bg-card">
                <TableRow className="hover:bg-transparent">
                  <TableHead className="pl-4">Started</TableHead>
                  <TableHead className="hidden sm:table-cell">Duration</TableHead>
                  <TableHead className="w-28 pr-4 sm:w-auto sm:pr-2">Status</TableHead>
                  <TableHead className="hidden pr-4 sm:table-cell">Result</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {executions?.map((execution) => {
                  const result = describeResult(execution)
                  const selected = execution.id === selectedId
                  return (
                    <TableRow
                      key={execution.id}
                      data-state={selected ? "selected" : undefined}
                    >
                      <TableCell className="pl-4">
                        <Button
                          variant="link"
                          aria-current={selected ? "true" : undefined}
                          onClick={() => select(execution.id)}
                          className="h-auto p-0 text-left text-foreground"
                        >
                          {when(execution.startedAt ?? execution.createdAt)}
                        </Button>
                        <p className="mt-0.5 text-xs whitespace-normal text-muted-foreground sm:hidden">
                          {elapsed(execution)} · {result.main}
                        </p>
                      </TableCell>
                      <TableCell className="hidden sm:table-cell">{elapsed(execution)}</TableCell>
                      <TableCell className="pr-4 sm:pr-2">
                        <StatusBadge execution={execution} />
                      </TableCell>
                      <TableCell className="hidden max-w-72 pr-4 whitespace-normal sm:table-cell">
                        <p>{result.main}</p>
                        {result.sub && (
                          <p className="text-muted-foreground">{result.sub}</p>
                        )}
                      </TableCell>
                    </TableRow>
                  )
                })}
                {executions?.length === 0 && (
                  <TableRow className="hover:bg-transparent">
                    <TableCell colSpan={4} className="p-6 text-center text-muted-foreground">
                      No executions yet. Start a task to see it here.
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </div>
        </section>

        <ExecutionDetails state={state} />
      </div>

      <IdentificationHistory />
    </section>
  )
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right">{children}</dd>
    </div>
  )
}

function DetailsPlaceholder({
  selectedId,
  detailsError,
}: Pick<JobsState, "selectedId" | "detailsError">) {
  if (selectedId === null)
    return (
      <p className="text-sm text-muted-foreground">
        Select an execution to see its details.
      </p>
    )
  if (detailsError)
    return (
      <p role="alert" className="text-sm text-destructive">
        {detailsError}
      </p>
    )
  return <Skeleton className="h-32 w-full" />
}

function ExecutionDetails({ state }: { state: JobsState }) {
  const { details, detailsRevision, detailsError, selectedId } = state
  const version = details ? executionVersion(details.execution, detailsRevision) : ""
  return (
    <section
      aria-labelledby="details-heading"
      className="flex min-h-64 min-w-0 flex-col gap-4 overflow-auto rounded-xl border bg-card p-4"
    >
      <div className="flex items-center justify-between gap-2">
        <h2 id="details-heading" className="text-lg font-semibold">
          Execution details
        </h2>
        {details && <StatusBadge execution={details.execution} />}
      </div>
      {!details ? (
        <DetailsPlaceholder selectedId={selectedId} detailsError={detailsError} />
      ) : (
        <>
          <dl className="flex flex-col gap-2 text-sm">
            <Fact label="Started">{when(details.execution.startedAt)}</Fact>
            <Fact label="Finished">{when(details.execution.finishedAt)}</Fact>
            <Fact label="Duration">{elapsed(details.execution)}</Fact>
            {details.execution.summary &&
              summaryFacts(details.execution.summary).map(([label, value]) => (
                <Fact key={label} label={label}>
                  {value}
                </Fact>
              ))}
          </dl>
          {details.execution.error && (
            <p role="alert" className="text-sm break-words text-destructive">
              {details.errorType ? `${details.errorType}: ` : ""}
              {details.execution.error}
            </p>
          )}
          {details.execution.kind === "missing-music-acquisition" && (
            <MissingMusicResults execution={details.execution} version={version} />
          )}
          <IdentificationOutcomes executionId={details.execution.id} version={version} />
          <div>
            <h3 className="mb-2 text-sm font-semibold">State history</h3>
            <ol className="flex flex-col gap-1 rounded-lg border bg-muted/40 p-3 font-mono text-xs">
              {details.history.map((entry, index) => (
                <li key={index} className="break-words">
                  <span className="text-muted-foreground">
                    {new Date(entry.at).toLocaleTimeString()}
                  </span>{" "}
                  {entry.state}
                  {entry.reason ? `: ${entry.reason}` : ""}
                </li>
              ))}
            </ol>
          </div>
        </>
      )}
    </section>
  )
}
