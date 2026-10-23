import { ResultRow } from "@/components/result-row"
import { Badge } from "@/components/ui/badge"
import {
  outcomeSubject,
  type FileOutcome,
  type IdentificationDecision,
  type IdentificationMethodStatus,
} from "@/lib/jobs-api"
import { decisionLabels } from "@/lib/job-summary"

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
