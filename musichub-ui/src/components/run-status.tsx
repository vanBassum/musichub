import { Badge } from "@/components/ui/badge"
import { formatElapsed } from "@/lib/format"
import { summaryFailures } from "@/lib/job-summary"
import type { JobExecution } from "@/lib/jobs-api"

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

// How long a run took (or has taken so far); a dash before it started.
export function elapsed(execution: JobExecution) {
  if (!execution.startedAt) return "–"
  const end = execution.finishedAt ? new Date(execution.finishedAt) : new Date()
  return formatElapsed(end.getTime() - new Date(execution.startedAt).getTime())
}

export function when(iso: string | null) {
  return iso ? new Date(iso).toLocaleString() : "–"
}

export function StatusBadge({ execution }: { execution: JobExecution }) {
  const outcome = outcomeOf(execution)
  return (
    <Badge className={outcomeClassNames[outcome]}>{outcomeLabels[outcome]}</Badge>
  )
}
