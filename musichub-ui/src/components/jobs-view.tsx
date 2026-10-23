import { Play, RefreshCw } from "lucide-react"
import { useState } from "react"

import { RunDetails } from "@/components/run-details"
import { elapsed, StatusBadge, when } from "@/components/run-status"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import {
  catalogReconciliationTaskId,
  jobFilters,
  type JobExecution,
  type JobFilter,
  type JobSchedule,
  type JobTask,
} from "@/lib/jobs-api"
import { syncJobsHash } from "@/lib/navigation"
import type { JobsState } from "@/lib/use-jobs"
import { cn } from "@/lib/utils"

const filterLabels: Record<JobFilter, string> = {
  all: "All runs",
  failed: "Failed",
  stuck: "Stuck",
}

// When a task runs next, or why it has no schedule. Nothing for a task that cannot be scheduled.
function ScheduleText({ schedule }: { schedule: JobSchedule | null }) {
  if (!schedule) return null
  if (schedule.problem)
    return (
      <span role="alert" className="text-destructive">
        {schedule.problem}
      </span>
    )
  return (
    <span className="text-muted-foreground">
      {schedule.enabled ? `Next run ${when(schedule.nextRun)}` : "Not scheduled"}
    </span>
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
    <li className="flex flex-col gap-2 px-3 py-2 text-sm">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <div className="min-w-0 flex-1 basis-56">
          <h3 className="font-medium break-words">{task.name}</h3>
          <p className="text-muted-foreground break-words">{task.description}</p>
        </div>
        <p className="flex flex-col sm:text-right">
          <ScheduleText schedule={task.schedule} />
          {running && <span className="font-medium text-primary">Running</span>}
        </p>
        <Button
          size="sm"
          aria-label={`Start ${task.name} now`}
          onClick={() => start(task.id, hasReIdentifyOption ? { reIdentifyAll } : undefined)}
          disabled={running || starting !== null}
        >
          <Play data-icon="inline-start" />
          {running ? "Running…" : "Start now"}
        </Button>
      </div>
      {hasReIdentifyOption && (
        <label className="flex items-start gap-2">
          <Checkbox
            checked={reIdentifyAll}
            onCheckedChange={setReIdentifyAll}
            disabled={running}
            className="mt-0.5"
          />
          <span>
            Re-identify all files
            <span className="block text-muted-foreground">
              Also identifies files that are already linked. Their link is never changed; each
              attempt is recorded.
            </span>
          </span>
        </label>
      )}
    </li>
  )
}

function AvailableJobs({ state }: { state: JobsState }) {
  const { tasks, starting, start } = state
  return (
    <section aria-labelledby="available-heading" className="flex min-w-0 flex-col gap-2">
      <h2 id="available-heading" className="text-lg font-semibold">
        Available jobs
      </h2>
      {tasks === null ? (
        <Skeleton className="h-24 w-full rounded-xl" />
      ) : tasks.length === 0 ? (
        <p className="text-muted-foreground">No background jobs are registered.</p>
      ) : (
        <ul aria-label="Available jobs" className="divide-y rounded-xl border bg-card">
          {tasks.map((task) => (
            <TaskRow key={task.id} task={task} starting={starting} start={start} />
          ))}
        </ul>
      )}
    </section>
  )
}

const runColumns = "grid-cols-2 sm:grid-cols-[11rem_minmax(0,1fr)_6rem_6.5rem]"

function RunRow({
  execution,
  title,
  selected,
  select,
}: {
  execution: JobExecution
  title: string
  selected: boolean
  select: JobsState["select"]
}) {
  return (
    <li>
      <button
        type="button"
        aria-current={selected ? "true" : undefined}
        onClick={() => select(execution.id)}
        className={cn(
          "grid w-full items-center gap-x-3 gap-y-0.5 border-l-2 border-transparent px-3 py-2 text-left text-sm hover:bg-muted/60 focus-visible:bg-muted/60",
          runColumns,
          selected && "border-primary bg-muted"
        )}
      >
        <span className="text-muted-foreground tabular-nums">
          {when(execution.startedAt ?? execution.createdAt)}
        </span>
        <span className="truncate font-medium" title={title}>
          {title}
        </span>
        <span className="text-muted-foreground tabular-nums sm:text-right">
          {elapsed(execution)}
        </span>
        <span className="justify-self-end">
          <StatusBadge execution={execution} />
        </span>
      </button>
    </li>
  )
}

function RecentRuns({ state, jobName }: { state: JobsState; jobName: (kind: string) => string }) {
  const { executions, total, filter, setFilter, canShowMore, showMore, selectedId, select } = state
  const changeFilter = (value: JobFilter) => {
    setFilter(value)
    syncJobsHash(value)
  }

  return (
    <section aria-labelledby="executions-heading" className="flex min-w-0 flex-col gap-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="executions-heading" className="text-lg font-semibold">
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
        <p role="status" className="text-sm text-muted-foreground">
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
        <ul aria-label="Recent runs" className="divide-y rounded-xl border bg-card">
          {executions.map((execution) => (
            <RunRow
              key={execution.id}
              execution={execution}
              title={jobName(execution.kind)}
              selected={execution.id === selectedId}
              select={select}
            />
          ))}
        </ul>
      )}
      {canShowMore && (
        <Button variant="outline" onClick={showMore}>
          Show more runs
        </Button>
      )}
    </section>
  )
}

// One page scroll holds everything: the jobs, the runs and the run details never scroll on their own. On desktop the
// jobs and runs sit left of the selected run; on a narrow screen they stack, and an opened run replaces them.
export function JobsView({
  state,
  className,
}: {
  state: JobsState
  className?: string
}) {
  const { refresh, refreshing, loadError, startError, tasks, detailOpen } = state
  // A run whose job is no longer registered keeps its kind as its title.
  const jobName = (kind: string) => tasks?.find((task) => task.id === kind)?.name ?? kind

  return (
    <section
      aria-labelledby="jobs-heading"
      className={cn("flex min-h-0 min-w-0 flex-col gap-4 overflow-auto", className)}
    >
      <div className="flex shrink-0 flex-wrap items-center gap-3">
        <div>
          <h1 id="jobs-heading" className="text-xl font-semibold">
            Jobs
          </h1>
          <p className="text-muted-foreground">
            View and start the background jobs of MusicHub
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

      <div className="grid shrink-0 items-start gap-4 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
        <div className={cn("flex min-w-0 flex-col gap-6", detailOpen && "hidden lg:flex")}>
          <AvailableJobs state={state} />
          <RecentRuns state={state} jobName={jobName} />
        </div>
        <RunDetails
          state={state}
          jobName={jobName}
          className={cn(!detailOpen && "hidden lg:flex")}
        />
      </div>
    </section>
  )
}
