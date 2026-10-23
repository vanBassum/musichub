import { ArrowLeft, ChevronRight, Play, RefreshCw } from "lucide-react"
import { useState, type ReactNode } from "react"

import { IdentificationOutcomes } from "@/components/identification-outcomes"
import { MissingMusicResults } from "@/components/missing-music-results"
import { RunLog } from "@/components/run-log"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  catalogReconciliationTaskId,
  jobFilters,
  missingMusicTaskId,
  type JobExecution,
  type JobFilter,
  type JobSchedule,
  type JobTask,
} from "@/lib/jobs-api"
import { formatElapsed } from "@/lib/format"
import {
  executionVersion,
  summaryFacts,
  summaryFailures,
  summaryHeadline,
} from "@/lib/job-summary"
import { syncJobsHash } from "@/lib/navigation"
import type { JobsState } from "@/lib/use-jobs"
import { cn } from "@/lib/utils"

type Outcome =
  | "waiting"
  | "stuck"
  | "retrying"
  | "running"
  | "completed"
  | "partial"
  | "failed"
  | "deleted"

const outcomeLabels: Record<Outcome, string> = {
  waiting: "Waiting",
  stuck: "Stuck",
  retrying: "Retrying",
  running: "Running",
  completed: "Completed",
  partial: "Partial",
  failed: "Failed",
  deleted: "Deleted",
}

const outcomeClassNames: Record<Outcome, string> = {
  waiting: "bg-muted text-muted-foreground",
  stuck: "bg-warning text-warning-foreground",
  retrying: "bg-warning text-warning-foreground",
  running: "bg-primary text-primary-foreground",
  completed: "bg-success text-success-foreground",
  partial: "bg-warning text-warning-foreground",
  failed: "bg-destructive text-white",
  deleted: "bg-muted text-muted-foreground",
}

const filterLabels: Record<JobFilter, string> = {
  all: "All runs",
  failed: "Failed",
  stuck: "Stuck",
}

// A run that Succeeded but could not process some files is shown as Partial; one that waits or processes for too
// long is shown as Stuck.
function outcomeOf(execution: JobExecution): Outcome {
  if (execution.stuck) return "stuck"
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

function elapsed(execution: JobExecution) {
  if (!execution.startedAt) return "–"
  const end = execution.finishedAt ? new Date(execution.finishedAt) : new Date()
  return formatElapsed(end.getTime() - new Date(execution.startedAt).getTime())
}

function when(iso: string | null) {
  return iso ? new Date(iso).toLocaleString() : "–"
}

// A run is titled by its task; a task that is no longer registered falls back to the run's kind.
function runTitle(tasks: JobTask[] | null, execution: JobExecution) {
  return tasks?.find((task) => task.id === execution.kind)?.name ?? execution.kind
}

function StatusBadge({ execution }: { execution: JobExecution }) {
  const outcome = outcomeOf(execution)
  return (
    <Badge className={outcomeClassNames[outcome]}>{outcomeLabels[outcome]}</Badge>
  )
}

// When a task runs next, or why it has no schedule. Never a time for a task that is not scheduled.
function NextRun({ schedule }: { schedule: JobSchedule | null }) {
  if (schedule?.problem)
    return (
      <span role="alert" className="text-destructive">
        {schedule.problem}
      </span>
    )
  if (!schedule?.enabled || !schedule.nextRun)
    return <span className="text-muted-foreground">Not scheduled</span>
  return (
    <>
      {when(schedule.nextRun)}
      <span className="block text-muted-foreground">cron {schedule.cron} (UTC)</span>
    </>
  )
}

// A task and its start button. One start is in flight at a time, and a task refuses a second run while one is active.
function TaskRow({
  task,
  starting,
  start,
}: {
  task: JobTask
  starting: JobsState["starting"]
  start: JobsState["start"]
}) {
  const running = starting === task.id || task.activeExecutionId !== null
  const [reIdentifyAll, setReIdentifyAll] = useState(false)
  const hasReIdentifyOption = task.id === catalogReconciliationTaskId
  return (
    <TableRow>
      <TableCell className="max-w-0 min-w-40 whitespace-normal">
        <span className="flex items-center gap-2 font-medium break-words">
          {task.name}
          {running && <Badge className={outcomeClassNames.running}>Running</Badge>}
        </span>
        <span className="block text-muted-foreground break-words">{task.description}</span>
        {hasReIdentifyOption && (
          <label className="mt-1 flex items-center gap-2">
            <Checkbox
              checked={reIdentifyAll}
              onCheckedChange={setReIdentifyAll}
              disabled={running}
            />
            Re-identify all files (links are never changed)
          </label>
        )}
      </TableCell>
      <TableCell className="whitespace-normal">
        <NextRun schedule={task.schedule} />
      </TableCell>
      <TableCell className="text-right">
        <Button
          size="sm"
          onClick={() => start(task.id, hasReIdentifyOption ? { reIdentifyAll } : undefined)}
          disabled={running || starting !== null}
        >
          <Play data-icon="inline-start" />
          {running ? "Running…" : "Start now"}
        </Button>
      </TableCell>
    </TableRow>
  )
}

function AvailableJobs({ state }: { state: JobsState }) {
  const { tasks, starting, start } = state
  return (
    <section
      aria-labelledby="available-jobs-heading"
      className="flex min-w-0 flex-col gap-2 rounded-xl border bg-card p-3"
    >
      <h2 id="available-jobs-heading" className="font-semibold">
        Available jobs
      </h2>
      {tasks === null ? (
        <Skeleton className="h-20 w-full" />
      ) : tasks.length === 0 ? (
        <p className="text-sm text-muted-foreground">No background jobs are registered.</p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Job</TableHead>
              <TableHead>Next run</TableHead>
              <TableHead className="text-right">
                <span className="sr-only">Action</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {tasks.map((task) => (
              <TaskRow key={task.id} task={task} starting={starting} start={start} />
            ))}
          </TableBody>
        </Table>
      )}
    </section>
  )
}

// One page scroll holds everything: the lists and the run details never scroll on their own.
export function JobsView({
  state,
  className,
}: {
  state: JobsState
  className?: string
}) {
  const { refresh, refreshing, loadError, startError, detailOpen } = state

  return (
    <section
      aria-labelledby="jobs-heading"
      className={cn("flex min-h-0 min-w-0 flex-col gap-4 overflow-auto", className)}
    >
      <div className="flex shrink-0 flex-wrap items-center gap-3">
        <h1 id="jobs-heading" className="text-xl font-semibold">
          Jobs
        </h1>
        <Button
          variant="outline"
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

      {/* Wide screens show the lists and the run details side by side; on a narrow one an opened run replaces the lists. */}
      <div className="grid shrink-0 items-start gap-4 text-sm lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className={cn("flex min-w-0 flex-col gap-4", detailOpen && "hidden lg:flex")}>
          <AvailableJobs state={state} />
          <RecentRuns state={state} />
        </div>
        <RunDetails state={state} className={cn(!detailOpen && "hidden lg:flex")} />
      </div>
    </section>
  )
}

function RecentRuns({ state }: { state: JobsState }) {
  const { tasks, executions, total, filter, setFilter, canShowMore, showMore, selectedId, select } =
    state
  const changeFilter = (value: JobFilter) => {
    setFilter(value)
    syncJobsHash(value)
  }

  return (
    <section
      aria-labelledby="executions-heading"
      className="flex min-w-0 flex-col gap-2 rounded-xl border bg-card p-3"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="executions-heading" className="font-semibold">
          Recent runs
        </h2>
        <Select value={filter} onValueChange={(value) => changeFilter(value as JobFilter)}>
          <SelectTrigger aria-label="Filter runs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {jobFilters.map((item) => (
              <SelectItem key={item} value={item}>
                {filterLabels[item]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      {total !== null && (
        <p role="status" className="text-muted-foreground">
          {total} {total === 1 ? "run matches" : "runs match"} this filter
        </p>
      )}
      {executions === null ? (
        <Skeleton className="h-24 w-full" />
      ) : executions.length === 0 ? (
        <p className="text-muted-foreground">
          {filter === "all"
            ? "No runs yet. Start a job to see it here."
            : "No runs match this filter."}
        </p>
      ) : (
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Time</TableHead>
              <TableHead>Job</TableHead>
              <TableHead>Runtime</TableHead>
              <TableHead>Status</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {executions.map((execution) => {
              const selected = execution.id === selectedId
              return (
                <TableRow
                  key={execution.id}
                  data-state={selected ? "selected" : undefined}
                  className="cursor-pointer data-[state=selected]:bg-primary/15"
                  onClick={() => select(execution.id)}
                >
                  <TableCell>
                    <button
                      type="button"
                      aria-current={selected ? "true" : undefined}
                      onClick={(event) => {
                        event.stopPropagation()
                        select(execution.id)
                      }}
                      className="rounded-sm text-left focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                    >
                      {when(execution.startedAt ?? execution.createdAt)}
                    </button>
                  </TableCell>
                  <TableCell className="max-w-0 min-w-32 break-words whitespace-normal">
                    {runTitle(tasks, execution)}
                  </TableCell>
                  <TableCell>{elapsed(execution)}</TableCell>
                  <TableCell>
                    <StatusBadge execution={execution} />
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      )}
      {canShowMore && (
        <Button variant="outline" onClick={showMore}>
          Show more runs
        </Button>
      )}
    </section>
  )
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex justify-between gap-4">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right break-words">{children}</dd>
    </div>
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

// The numbers and the per-file and per-download results of a run, one click away: the log above is the summary.
function TechnicalDetails({
  execution,
  version,
}: {
  execution: JobExecution
  version: string
}) {
  const [open, setOpen] = useState(false)
  return (
    <Collapsible open={open} onOpenChange={setOpen} className="group">
      <CollapsibleTrigger className="flex items-center gap-1 text-sm font-semibold">
        <ChevronRight
          aria-hidden="true"
          className="size-4 transition-transform group-data-[open]:rotate-90"
        />
        Technical details
      </CollapsibleTrigger>
      <CollapsibleContent className="mt-2 flex flex-col gap-4">
        {open && (
          <>
            {execution.summary && (
              <dl className="flex flex-col gap-1">
                {summaryFacts(execution.summary).map(([label, value]) => (
                  <Fact key={label} label={label}>
                    {value}
                  </Fact>
                ))}
              </dl>
            )}
            {execution.kind === missingMusicTaskId && (
              <MissingMusicResults execution={execution} version={version} />
            )}
            <IdentificationOutcomes executionId={execution.id} version={version} />
          </>
        )}
      </CollapsibleContent>
    </Collapsible>
  )
}

function RunDetails({ state, className }: { state: JobsState; className?: string }) {
  const { tasks, details, detailsRevision, detailsError, selectedId, closeDetails } = state
  const version = details ? executionVersion(details.execution, detailsRevision) : ""
  return (
    <section
      aria-labelledby="details-heading"
      className={cn("flex min-w-0 flex-col gap-3 rounded-xl border bg-card p-3", className)}
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
          <h2 id="details-heading" className="font-semibold">
            Run details
          </h2>
        </div>
        {details && <StatusBadge execution={details.execution} />}
      </div>
      {!details ? (
        <DetailsPlaceholder selectedId={selectedId} detailsError={detailsError} />
      ) : (
        <>
          <p className="text-base font-medium break-words">
            {runTitle(tasks, details.execution)}
          </p>
          <dl className="flex flex-col gap-1">
            <Fact label="Started">{when(details.execution.startedAt)}</Fact>
            <Fact label="Finished">{when(details.execution.finishedAt)}</Fact>
            <Fact label="Runtime">{elapsed(details.execution)}</Fact>
            {details.execution.summary && (
              <Fact label="Result">{summaryHeadline(details.execution.summary).main}</Fact>
            )}
          </dl>
          {details.execution.error && (
            <p role="alert" className="break-words text-destructive">
              {details.errorType ? `${details.errorType}: ` : ""}
              {details.execution.error}
            </p>
          )}
          <RunLog key={details.execution.id} details={details} version={version} />
          <TechnicalDetails
            key={details.execution.id}
            execution={details.execution}
            version={version}
          />
        </>
      )}
    </section>
  )
}
