import { ResultRow } from "@/components/result-row"
import { Badge } from "@/components/ui/badge"
import { Skeleton } from "@/components/ui/skeleton"
import {
  outcomeSubject,
  type FileOutcome,
  type IdentificationDecision,
  type IdentificationMethodStatus,
} from "@/lib/jobs-api"
import { decisionLabels } from "@/lib/job-summary"
import { useOutcomes } from "@/lib/use-outcomes"

export const decisionClassNames: Record<IdentificationDecision, string> = {
  Identified: "bg-success text-success-foreground",
  NoMatch: "bg-muted text-muted-foreground",
  InsufficientEvidence: "bg-warning text-warning-foreground",
  NoMethodRan: "bg-warning text-warning-foreground",
  MethodFailed: "bg-destructive text-white",
  Skipped: "bg-muted text-muted-foreground",
  Error: "bg-destructive text-white",
  Mismatch: "bg-warning text-warning-foreground",
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

export function FileOutcomeItem({ outcome }: { outcome: FileOutcome }) {
  return (
    <ResultRow
      title={outcome.fileName || outcomeSubject(outcome)}
      summary={outcome.reason}
      badge={
        <Badge className={decisionClassNames[outcome.decision]}>
          {decisionLabels[outcome.decision]}
        </Badge>
      }
    >
      {outcome.methods.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No identification method produced a result for this attempt.
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
    </ResultRow>
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
      <h3 className="mb-2 text-sm font-semibold">Identification</h3>
      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      {outcomes === null ? (
        !error && <Skeleton className="h-16 w-full" />
      ) : outcomes.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No identification attempts are recorded for this execution.
        </p>
      ) : (
        <div className="flex flex-col gap-3">
          <Counts outcomes={outcomes} />
          <ul aria-label="File outcomes" className="flex flex-col gap-2">
            {outcomes.map((outcome) => (
              <FileOutcomeItem key={outcomeSubject(outcome)} outcome={outcome} />
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
