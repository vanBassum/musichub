import { useEffect, useState } from "react"

import { getOutcomes, type FileOutcome } from "@/lib/jobs-api"

export interface OutcomesState {
  outcomes: FileOutcome[] | null
  error: string | null
}

// The per-file outcomes of one execution. They are fetched again when `version` changes (the execution moved
// on), so a running execution keeps up; the previous list stays visible meanwhile. Outcomes of another
// execution are never shown.
export function useOutcomes(executionId: string, version: string): OutcomesState {
  const [loaded, setLoaded] = useState<{ id: string; outcomes: FileOutcome[] } | null>(null)
  const [failed, setFailed] = useState<{ id: string; message: string } | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    getOutcomes(executionId, controller.signal).then(
      (outcomes) => {
        if (controller.signal.aborted) return
        setLoaded({ id: executionId, outcomes })
        setFailed(null)
      },
      (error: unknown) => {
        if (controller.signal.aborted) return
        setFailed({
          id: executionId,
          message: error instanceof Error ? error.message : "Loading failed.",
        })
      }
    )
    return () => controller.abort()
  }, [executionId, version])

  return {
    outcomes: loaded?.id === executionId ? loaded.outcomes : null,
    error: failed?.id === executionId ? failed.message : null,
  }
}
