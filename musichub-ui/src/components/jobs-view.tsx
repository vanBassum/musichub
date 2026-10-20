import { ArrowLeft, Database, Play, RefreshCw } from "lucide-react"
import { useState, type ReactNode } from "react"

import { IdentificationOutcomes } from "@/components/identification-outcomes"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import type { JobExecution, JobSummary } from "@/lib/jobs-api"
import { formatElapsed } from "@/lib/format"
import type { JobsState, JobsTab } from "@/lib/use-jobs"
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

const allStatuses = "all"
type StatusFilter = Outcome | typeof allStatuses

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

// One page scroll holds everything: the tabs, the run list and the run details never scroll on their own.
export function JobsView({
  state,
  className,
}: {
  state: JobsState
  className?: string
}) {
  const { tab, setTab, refresh, refreshing, loadError, startError } = state

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
        <Button
          variant="outline"
          size="lg"
          className="ml-auto"
          onClick={() => void refresh()}
          disabled={refreshing}
        >
          <RefreshCw data-icon="inline-start" />
          Refresh
        </Button>
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

      <Tabs
        value={tab}
        onValueChange={(value) => setTab(value as JobsTab)}
        className="shrink-0 gap-4"
      >
        <TabsList className="w-full sm:w-fit">
          <TabsTrigger value="tasks" className="px-4">
            Tasks
          </TabsTrigger>
          <TabsTrigger value="history" className="px-4">
            History
          </TabsTrigger>
        </TabsList>
        <TabsContent value="tasks" className="text-sm">
          <TasksTab state={state} />
        </TabsContent>
        <TabsContent value="history" className="text-sm">
          <HistoryTab state={state} />
        </TabsContent>
      </Tabs>
    </section>
  )
}

function TasksTab({ state }: { state: JobsState }) {
  const { tasks, starting, start } = state
  const task = tasks?.[0]
  const running = starting || task?.activeExecutionId != null

  if (tasks === null) return <Skeleton className="h-28 w-full max-w-xl rounded-xl" />
  return (
    <ul aria-label="Tasks" className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
      {tasks.map((item) => (
        <li
          key={item.id}
          className="flex items-start gap-3 rounded-xl border border-primary bg-card p-4"
        >
          <span className="rounded-lg bg-primary/10 p-2 text-primary">
            <Database aria-hidden="true" />
          </span>
          <div className="flex min-w-0 flex-1 flex-col gap-2">
            <div>
              <h2 className="font-semibold">{item.name}</h2>
              <p className="text-muted-foreground">{item.description}</p>
            </div>
            <p className="flex items-center gap-2">
              <span
                className={cn(
                  "size-2 rounded-full",
                  item.activeExecutionId ? "bg-primary" : "bg-success"
                )}
              />
              {item.activeExecutionId ? "Running" : "Available"}
            </p>
            <Button className="w-fit" onClick={start} disabled={running}>
              <Play data-icon="inline-start" />
              {running ? "Synchronizing…" : "Synchronize catalog"}
            </Button>
          </div>
        </li>
      ))}
    </ul>
  )
}

// Master-detail on desktop; on a narrow screen the list and the opened run replace each other.
function HistoryTab({ state }: { state: JobsState }) {
  const { detailOpen } = state
  return (
    <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
      <RunList state={state} className={cn(detailOpen && "hidden lg:flex")} />
      <RunDetails state={state} className={cn(!detailOpen && "hidden lg:flex")} />
    </div>
  )
}

function RunList({ state, className }: { state: JobsState; className?: string }) {
  const { executions, selectedId, select } = state
  const [status, setStatus] = useState<StatusFilter>(allStatuses)
  const shown = executions?.filter(
    (execution) => status === allStatuses || outcomeOf(execution) === status
  )

  return (
    <section
      aria-labelledby="executions-heading"
      className={cn("flex min-w-0 flex-col gap-3 rounded-xl border bg-card p-4", className)}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="executions-heading" className="text-lg font-semibold">
          Runs
        </h2>
        <Select value={status} onValueChange={(value) => setStatus(value as StatusFilter)}>
          <SelectTrigger aria-label="Filter by status">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={allStatuses}>All statuses</SelectItem>
            {(Object.keys(outcomeLabels) as Outcome[]).map((item) => (
              <SelectItem key={item} value={item}>
                {outcomeLabels[item]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      {shown === undefined ? (
        <Skeleton className="h-24 w-full" />
      ) : shown.length === 0 ? (
        <p className="text-muted-foreground">
          {executions?.length === 0
            ? "No runs yet. Start a task to see it here."
            : "No runs have this status."}
        </p>
      ) : (
        <ul aria-label="Runs" className="flex flex-col gap-2">
          {shown.map((execution) => {
            const result = describeResult(execution)
            const selected = execution.id === selectedId
            return (
              <li key={execution.id}>
                <button
                  type="button"
                  aria-current={selected ? "true" : undefined}
                  onClick={() => select(execution.id)}
                  className={cn(
                    "flex w-full flex-col gap-1 rounded-lg border p-3 text-left hover:bg-muted/60",
                    selected && "border-primary bg-muted/60"
                  )}
                >
                  <span className="flex items-center justify-between gap-2">
                    <span className="font-medium">
                      {when(execution.startedAt ?? execution.createdAt)}
                    </span>
                    <StatusBadge execution={execution} />
                  </span>
                  <span className="break-words text-muted-foreground">
                    {elapsed(execution)} · {result.main}
                  </span>
                  {result.sub && (
                    <span className="line-clamp-2 break-words text-muted-foreground">
                      {result.sub}
                    </span>
                  )}
                </button>
              </li>
            )
          })}
        </ul>
      )}
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
    return <p className="text-muted-foreground">Select a run to see its details.</p>
  if (detailsError)
    return (
      <p role="alert" className="text-destructive">
        {detailsError}
      </p>
    )
  return <Skeleton className="h-32 w-full" />
}

function RunDetails({ state, className }: { state: JobsState; className?: string }) {
  const { details, detailsError, selectedId, closeDetails } = state
  return (
    <section
      aria-labelledby="details-heading"
      className={cn("flex min-w-0 flex-col gap-4 rounded-xl border bg-card p-4", className)}
    >
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="icon"
            aria-label="Back to runs"
            className="lg:hidden"
            onClick={closeDetails}
          >
            <ArrowLeft />
          </Button>
          <h2 id="details-heading" className="text-lg font-semibold">
            Run details
          </h2>
        </div>
        {details && <StatusBadge execution={details.execution} />}
      </div>
      {!details ? (
        <DetailsPlaceholder selectedId={selectedId} detailsError={detailsError} />
      ) : (
        <>
          <dl className="flex flex-col gap-2">
            <Fact label="Started">{when(details.execution.startedAt)}</Fact>
            <Fact label="Finished">{when(details.execution.finishedAt)}</Fact>
            <Fact label="Duration">{elapsed(details.execution)}</Fact>
            {details.execution.summary && (
              <SummaryFacts summary={details.execution.summary} />
            )}
          </dl>
          {details.execution.error && (
            <p role="alert" className="break-words text-destructive">
              {details.errorType ? `${details.errorType}: ` : ""}
              {details.execution.error}
            </p>
          )}
          <IdentificationOutcomes
            executionId={details.execution.id}
            version={`${details.execution.state}:${details.execution.summary?.examined ?? ""}`}
          />
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
