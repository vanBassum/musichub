import { Database, Play, RefreshCw } from "lucide-react"
import type { ReactNode } from "react"

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
import type { JobExecution, JobSummary } from "@/lib/jobs-api"
import { formatElapsed } from "@/lib/format"
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
      return (execution.summary?.failed ?? 0) > 0 ? "partial" : "completed"
    case "Failed":
      return "failed"
    default:
      return "deleted"
  }
}

function describeResult(execution: JobExecution) {
  const { summary, error } = execution
  if (summary) {
    return {
      main: `${summary.examined} ${summary.examined === 1 ? "file" : "files"} examined`,
      sub: `${summary.linked} linked, ${summary.unresolved} unresolved${
        summary.failed > 0 ? `, ${summary.failed} failed` : ""
      }`,
    }
  }
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
  const task = tasks?.[0]
  const running = starting || task?.activeExecutionId != null

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
          <Button size="lg" onClick={start} disabled={running || !task}>
            <Play data-icon="inline-start" />
            {running ? "Synchronizing…" : "Synchronize catalog"}
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
              <li
                key={item.id}
                className="flex items-start gap-3 rounded-xl border border-primary bg-card p-4"
              >
                <span className="rounded-lg bg-primary/10 p-2 text-primary">
                  <Database aria-hidden="true" />
                </span>
                <div className="min-w-0">
                  <h2 className="font-semibold">{item.name}</h2>
                  <p className="text-sm text-muted-foreground">{item.description}</p>
                  <p className="mt-2 flex items-center gap-2 text-sm">
                    <span
                      className={cn(
                        "size-2 rounded-full",
                        item.activeExecutionId ? "bg-primary" : "bg-success"
                      )}
                    />
                    {item.activeExecutionId ? "Running" : "Available"}
                  </p>
                </div>
              </li>
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

function SummaryFacts({ summary }: { summary: JobSummary }) {
  return (
    <>
      <Fact label="Examined">{summary.examined} files</Fact>
      <Fact label="Linked">{summary.linked} files</Fact>
      <Fact label="Tracks created">{summary.tracksCreated}</Fact>
      <Fact label="Not identified">{summary.unresolved} files</Fact>
      <Fact label="Skipped">{summary.skipped} files</Fact>
      <Fact label="Errors">{summary.failed}</Fact>
    </>
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
  const { details, detailsError, selectedId } = state
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
            {details.execution.summary && (
              <SummaryFacts summary={details.execution.summary} />
            )}
          </dl>
          {details.execution.error && (
            <p role="alert" className="text-sm break-words text-destructive">
              {details.errorType ? `${details.errorType}: ` : ""}
              {details.execution.error}
            </p>
          )}
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
