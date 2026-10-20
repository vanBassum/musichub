import { ChevronRight } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import { Skeleton } from "@/components/ui/skeleton"
import type {
  FileOutcome,
  IdentificationDecision,
  IdentificationMethodStatus,
} from "@/lib/jobs-api"
import { useOutcomes } from "@/lib/use-outcomes"

export const decisionLabels: Record<IdentificationDecision, string> = {
  Identified: "Linked",
  NoMatch: "No match",
  InsufficientEvidence: "Not enough evidence",
  NoMethodRan: "No method could run",
  MethodFailed: "A method failed",
  Skipped: "Skipped",
  Error: "Error",
}

export const decisionClassNames: Record<IdentificationDecision, string> = {
  Identified: "bg-success text-success-foreground",
  NoMatch: "bg-muted text-muted-foreground",
  InsufficientEvidence: "bg-warning text-warning-foreground",
  NoMethodRan: "bg-warning text-warning-foreground",
  MethodFailed: "bg-destructive text-white",
  Skipped: "bg-muted text-muted-foreground",
  Error: "bg-destructive text-white",
}

const methodLabels: Record<IdentificationMethodStatus, string> = {
  Found: "Found candidates",
  NothingFound: "Nothing found",
  Unconfigured: "Not configured",
  Failed: "Failed",
  NotRun: "Not used",
}

const methodClassNames: Record<IdentificationMethodStatus, string> = {
  Found: "bg-primary text-primary-foreground",
  NothingFound: "bg-muted text-muted-foreground",
  Unconfigured: "bg-warning text-warning-foreground",
  Failed: "bg-destructive text-white",
  NotRun: "bg-muted text-muted-foreground",
}

function Counts({ outcomes }: { outcomes: FileOutcome[] }) {
  const counts = new Map<IdentificationDecision, number>()
  for (const outcome of outcomes)
    counts.set(outcome.decision, (counts.get(outcome.decision) ?? 0) + 1)
  return (
    <ul aria-label="Outcomes by decision" className="flex flex-wrap gap-2 text-xs">
      {[...counts].map(([decision, count]) => (
        <li key={decision}>
          <Badge className={decisionClassNames[decision]}>
            {count} {decisionLabels[decision].toLowerCase()}
          </Badge>
        </li>
      ))}
    </ul>
  )
}

// Where a recorded outcome comes from. The execution is only a label: Hangfire may have dropped it long ago.
function Provenance({ outcome }: { outcome: FileOutcome }) {
  return (
    <p className="mb-2 text-xs text-muted-foreground">
      Recorded {new Date(outcome.recordedAt).toLocaleString()} · execution{" "}
      {outcome.jobExecutionId || "not recorded"} · file {outcome.fileId} ·{" "}
      {outcome.trackId === null ? "no track linked" : `track ${outcome.trackId}`}
    </p>
  )
}

export function FileOutcomeItem({
  outcome,
  showProvenance = false,
}: {
  outcome: FileOutcome
  showProvenance?: boolean
}) {
  return (
    <li>
      <Collapsible className="group rounded-lg border bg-muted/40">
        <CollapsibleTrigger className="flex w-full items-start gap-2 p-2 text-left text-sm">
          <ChevronRight
            aria-hidden="true"
            className="mt-0.5 size-4 shrink-0 transition-transform group-data-[open]:rotate-90"
          />
          <span className="min-w-0 flex-1">
            <span className="block break-words font-medium">{outcome.fileName || `File ${outcome.fileId}`}</span>
            <span className="block break-words text-muted-foreground">{outcome.reason}</span>
          </span>
          <Badge className={decisionClassNames[outcome.decision]}>
            {decisionLabels[outcome.decision]}
          </Badge>
        </CollapsibleTrigger>
        <CollapsibleContent className="border-t p-2">
          {showProvenance && <Provenance outcome={outcome} />}
          {outcome.methods.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              No identification method produced a result for this file.
            </p>
          ) : (
            <ul aria-label={`Methods for ${outcome.fileName}`} className="flex flex-col gap-3 text-sm">
              {outcome.methods.map((method) => (
                <li key={method.source}>
                  <div className="flex items-center justify-between gap-2">
                    <span className="font-medium">{method.source}</span>
                    <Badge className={methodClassNames[method.status]}>
                      {methodLabels[method.status]}
                    </Badge>
                  </div>
                  <p className="break-words text-muted-foreground">{method.note}</p>
                  {method.candidates.length > 0 && (
                    <ul className="mt-1 list-disc pl-5 break-words">
                      {method.candidates.map((candidate, index) => (
                        <li key={index}>{candidate}</li>
                      ))}
                    </ul>
                  )}
                </li>
              ))}
            </ul>
          )}
        </CollapsibleContent>
      </Collapsible>
    </li>
  )
}

// What happened to each file the execution examined, with each file's identification methods one click away. The
// job's own state is separate: a Completed execution may have identified none of its files.
export function IdentificationOutcomes({
  executionId,
  version,
}: {
  executionId: string
  version: string
}) {
  const { outcomes, error } = useOutcomes(executionId, version)

  return (
    <div>
      <h3 className="mb-2 text-sm font-semibold">Files</h3>
      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      {outcomes === null ? (
        !error && <Skeleton className="h-16 w-full" />
      ) : outcomes.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No file outcomes are recorded for this execution.
        </p>
      ) : (
        <div className="flex flex-col gap-3">
          <Counts outcomes={outcomes} />
          <ul aria-label="File outcomes" className="flex flex-col gap-2">
            {outcomes.map((outcome) => (
              <FileOutcomeItem key={outcome.fileId} outcome={outcome} />
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
