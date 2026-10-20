import { useEffect, useState } from "react"

import {
  getIdentificationHistory,
  type IdentificationDecision,
  type OutcomePage,
} from "@/lib/jobs-api"

export const historyPageSize = 20

export interface IdentificationHistoryState {
  history: OutcomePage | null
  error: string | null
  decision: IdentificationDecision | null
  page: number
  setDecision: (decision: IdentificationDecision | null) => void
  setPage: (page: number) => void
}

// One page of the durable identification history. A change of filter returns to page 1; the previous page stays
// visible while the next loads, and a result that no longer matches the request is never shown.
export function useIdentificationHistory(): IdentificationHistoryState {
  const [request, setRequest] = useState<{
    decision: IdentificationDecision | null
    page: number
  }>({ decision: null, page: 1 })
  const [loaded, setLoaded] = useState<{
    request: typeof request
    history: OutcomePage
  } | null>(null)
  const [failed, setFailed] = useState<{ request: typeof request; message: string } | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    getIdentificationHistory({ ...request, pageSize: historyPageSize }, controller.signal).then(
      (history) => {
        if (controller.signal.aborted) return
        setLoaded({ request, history })
        setFailed(null)
      },
      (error: unknown) => {
        if (controller.signal.aborted) return
        setFailed({
          request,
          message: error instanceof Error ? error.message : "Loading failed.",
        })
      }
    )
    return () => controller.abort()
  }, [request])

  return {
    history: loaded?.request.decision === request.decision ? loaded.history : null,
    error: failed?.request === request ? failed.message : null,
    ...request,
    setDecision: (decision) => setRequest({ decision, page: 1 }),
    setPage: (page) => setRequest((current) => ({ ...current, page })),
  }
}
