import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  decisionLabels,
  FileOutcomeItem,
} from "@/components/identification-outcomes"
import { outcomeSubject, type IdentificationDecision } from "@/lib/jobs-api"
import {
  historyPageSize,
  useIdentificationHistory,
} from "@/lib/use-identification-history"

const unresolved = "unresolved"

// The decisions the filter offers besides the default, which is every unresolved one.
const filterDecisions: IdentificationDecision[] = [
  "NoMatch",
  "InsufficientEvidence",
  "NoMethodRan",
  "MethodFailed",
  "Error",
  "Mismatch",
  "Identified",
]

// A read-only, durable history of identification outcomes across job executions. It records what the
// identification did; it offers no confirmation, correction or reprocessing.
export function IdentificationHistory() {
  const { history, error, decision, page, setDecision, setPage } =
    useIdentificationHistory()
  const pages = history ? Math.max(1, Math.ceil(history.total / historyPageSize)) : 1

  return (
    <section
      aria-labelledby="history-heading"
      className="flex shrink-0 flex-col gap-3 rounded-xl border bg-card p-4"
    >
      <div className="flex flex-wrap items-center gap-3">
        <div className="min-w-0 flex-1">
          <h2 id="history-heading" className="text-lg font-semibold">
            Identification history
          </h2>
          <p className="text-sm text-muted-foreground">
            Recorded outcomes of every synchronization, kept after the execution expires.
          </p>
        </div>
        <Select
          value={decision ?? unresolved}
          onValueChange={(value) =>
            setDecision(value === unresolved ? null : (value as IdentificationDecision))
          }
        >
          <SelectTrigger aria-label="Filter by decision">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={unresolved}>All unresolved</SelectItem>
            {filterDecisions.map((item) => (
              <SelectItem key={item} value={item}>
                {decisionLabels[item]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}
      {history === null ? (
        !error && <Skeleton className="h-16 w-full" />
      ) : history.items.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          No outcomes are recorded for this filter.
        </p>
      ) : (
        <>
          <ul aria-label="Recorded outcomes" className="flex flex-col gap-2">
            {history.items.map((outcome) => (
              <FileOutcomeItem
                key={`${outcome.jobExecutionId}:${outcomeSubject(outcome)}`}
                outcome={outcome}
                showProvenance
              />
            ))}
          </ul>
          <div className="flex items-center justify-between gap-2 text-sm">
            <span className="text-muted-foreground">
              Page {page} of {pages} · {history.total} outcomes
            </span>
            <div className="flex gap-2">
              <Button variant="outline" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                Previous
              </Button>
              <Button variant="outline" disabled={page >= pages} onClick={() => setPage(page + 1)}>
                Next
              </Button>
            </div>
          </div>
        </>
      )}
    </section>
  )
}
