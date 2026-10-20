import { RefreshCw } from "lucide-react"

import {
  decisionClassNames,
  decisionLabels,
} from "@/components/identification-outcomes"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import type { IdentificationDecision } from "@/lib/jobs-api"
import type { Problem, ProblemSource } from "@/lib/problems-api"
import type { ProblemsState } from "@/lib/use-problems"
import { cn } from "@/lib/utils"

const sourceLabels: Record<ProblemSource, string> = {
  identification: "Identification",
  job: "Background task",
}

// The badge shows the source's own state: an identification decision, or a failed run.
function kindOf(problem: Problem) {
  if (problem.source === "job")
    return { label: "Failed", className: "bg-destructive text-white" }
  const decision = problem.kind as IdentificationDecision
  return {
    label: decisionLabels[decision] ?? problem.kind,
    className: decisionClassNames[decision] ?? "bg-muted text-muted-foreground",
  }
}

function ProblemItem({
  problem,
  onOpenExecution,
}: {
  problem: Problem
  onOpenExecution: (executionId: string) => void
}) {
  const kind = kindOf(problem)
  return (
    <li className="flex flex-wrap items-start gap-3 p-4">
      <div className="min-w-0 flex-1 basis-64">
        <p className="font-medium break-words">{problem.subject}</p>
        <p className="text-sm break-words text-muted-foreground">{problem.reason}</p>
        <p className="mt-1 text-xs text-muted-foreground">
          {sourceLabels[problem.source]} ·{" "}
          {new Date(problem.occurredAt).toLocaleString()}
        </p>
      </div>
      <div className="flex items-center gap-2">
        <Badge className={kind.className}>{kind.label}</Badge>
        {problem.jobExecutionId !== null && (
          <Button
            variant="outline"
            onClick={() => onOpenExecution(problem.jobExecutionId!)}
          >
            View execution
          </Button>
        )}
      </div>
    </li>
  )
}

function ProblemsBody({
  state,
  onOpenExecution,
}: {
  state: ProblemsState
  onOpenExecution: (executionId: string) => void
}) {
  const { problems, error } = state
  if (problems === null)
    return error ? null : <Skeleton className="h-40 w-full rounded-xl" />

  return (
    <>
      {problems.unavailable.length > 0 && (
        <p role="status" className="shrink-0 rounded-xl bg-warning px-4 py-3 text-sm text-warning-foreground">
          Not available right now:{" "}
          {problems.unavailable.map((source) => sourceLabels[source]).join(", ")}.
          Problems from there are missing from this list.
        </p>
      )}
      {problems.items.length === 0 ? (
        <p className="rounded-xl border bg-card p-6 text-center text-muted-foreground">
          No problems are recorded.
        </p>
      ) : (
        <ul
          aria-label="Problems"
          className="divide-y rounded-xl border bg-card"
        >
          {problems.items.map((problem) => (
            <ProblemItem
              key={problem.id}
              problem={problem}
              onOpenExecution={onOpenExecution}
            />
          ))}
        </ul>
      )}
    </>
  )
}

// Read-only overview of what identification and the background tasks already recorded as exceptional. Those
// records stay the source of truth: nothing here changes them.
export function ProblemsView({
  state,
  onOpenExecution,
  className,
}: {
  state: ProblemsState
  onOpenExecution: (executionId: string) => void
  className?: string
}) {
  return (
    <section
      aria-labelledby="problems-heading"
      className={cn("flex min-h-0 min-w-0 flex-col gap-4 overflow-auto", className)}
    >
      <div className="flex shrink-0 flex-wrap items-center gap-3">
        <div>
          <h1 id="problems-heading" className="text-xl font-semibold">
            Problems
          </h1>
          <p className="text-muted-foreground">
            Files that could not be identified and background tasks that failed
          </p>
        </div>
        <Button
          variant="outline"
          size="lg"
          className="ml-auto"
          onClick={() => void state.refresh()}
          disabled={state.refreshing}
        >
          <RefreshCw data-icon="inline-start" />
          Refresh
        </Button>
      </div>
      {state.error && (
        <p role="alert" className="shrink-0 text-sm text-destructive">
          {state.error}
        </p>
      )}
      <ProblemsBody state={state} onOpenExecution={onOpenExecution} />
    </section>
  )
}
