import { useCallback } from "react"

import { FileOutcomeItem } from "@/components/identification-outcomes"
import { Badge } from "@/components/ui/badge"
import { Skeleton } from "@/components/ui/skeleton"
import {
  getFileIdentification,
  identificationStates,
  type FileIdentification,
  type FileIdentificationState,
} from "@/lib/files-api"
import { useLoader } from "@/lib/use-loader"

const stateClassNames: Record<FileIdentificationState, string> = {
  Identified: "bg-success text-success-foreground",
  ManuallyConfirmed: "bg-success text-success-foreground",
  Unidentified: "bg-muted text-muted-foreground",
  NotAttempted: "bg-muted text-muted-foreground",
  Uncertain: "bg-warning text-warning-foreground",
  NoMatch: "bg-muted text-muted-foreground",
  NoMethodRan: "bg-warning text-warning-foreground",
  MethodFailed: "bg-destructive text-white",
  Error: "bg-destructive text-white",
  Mismatch: "bg-warning text-warning-foreground",
}

// Where a Stored file stands in identification; the hint says what the state means.
export function IdentificationBadge({
  identification,
}: {
  identification: FileIdentification
}) {
  const { label, hint } = identificationStates[identification.state]
  return (
    <Badge title={hint} className={stateClassNames[identification.state]}>
      {label}
    </Badge>
  )
}

// The state in words: who linked the file to which track, or how the latest attempt ended and why.
function Summary({ identification }: { identification: FileIdentification }) {
  const { state, source, trackTitle, reason } = identification
  if (source !== null)
    return (
      <p>
        Linked to {trackTitle ? `“${trackTitle}”` : "a track"}
        {source === "Manual" ? ", confirmed by a person." : " by identification."}
      </p>
    )
  return (
    <p>
      {identificationStates[state].hint}.{reason ? ` ${reason}` : ""}
    </p>
  )
}

// What is recorded about identifying one file: its state, every applied identity (automatic or manual) and every
// attempt with the evidence each method gave. Loaded when opened; history is only read, never changed.
export function FileIdentificationDetails({
  fileId,
  identification,
}: {
  fileId: string
  identification: FileIdentification
}) {
  const fetcher = useCallback(
    (signal: AbortSignal) => getFileIdentification(fileId, signal),
    [fileId]
  )
  const { data, error } = useLoader(fetcher)

  return (
    <div className="flex flex-col gap-3 text-sm">
      <Summary identification={identification} />
      {error && (
        <p role="alert" className="text-destructive">
          {error}
        </p>
      )}
      {data === null ? (
        !error && <Skeleton className="h-12 w-full" />
      ) : (
        <>
          {data.applied.length > 0 && (
            <div>
              <h4 className="mb-1 font-semibold">Applied identities</h4>
              <ul aria-label="Applied identities" className="flex flex-col gap-1">
                {data.applied.map((applied, index) => (
                  <li key={index} className="break-words">
                    {new Date(applied.appliedAt).toLocaleString()} ·{" "}
                    {applied.source === "Manual" ? "Confirmed by a person" : "Identification"}{" "}
                    → {applied.trackTitle ?? "a track that no longer exists"}
                  </li>
                ))}
              </ul>
            </div>
          )}
          <div>
            <h4 className="mb-1 font-semibold">Identification attempts</h4>
            {data.attempts.length === 0 ? (
              <p className="text-muted-foreground">
                No identification attempt is recorded for this file.
              </p>
            ) : (
              <ul aria-label="Identification attempts" className="flex flex-col gap-2">
                {data.attempts.map((attempt, index) => (
                  <FileOutcomeItem
                    key={`${attempt.jobExecutionId}:${index}`}
                    outcome={attempt}
                  />
                ))}
              </ul>
            )}
          </div>
        </>
      )}
    </div>
  )
}
