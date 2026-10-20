import { useCallback, useEffect, useRef, useState } from "react"

import { listProblems, type ProblemList } from "@/lib/problems-api"

export interface ProblemsState {
  problems: ProblemList | null
  error: string | null
  refreshing: boolean
  refresh: () => Promise<void>
}

// Loads the problem overview once and again on demand; only the newest request may write state. A failed refresh
// keeps the list that was loaded before, next to the error.
export function useProblems(): ProblemsState {
  const [problems, setProblems] = useState<ProblemList | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(true)
  const latest = useRef<AbortController | null>(null)

  const load = useCallback(() => {
    latest.current?.abort()
    const controller = new AbortController()
    latest.current = controller
    return listProblems(controller.signal)
      .then(
        (list) => {
          if (controller.signal.aborted) return
          setProblems(list)
          setError(null)
        },
        (failure: unknown) => {
          if (controller.signal.aborted) return
          setError(failure instanceof Error ? failure.message : "Loading failed.")
        }
      )
      .finally(() => {
        if (latest.current === controller) setRefreshing(false)
      })
  }, [])

  const refresh = useCallback(() => {
    setRefreshing(true)
    return load()
  }, [load])

  // `refreshing` starts true, so the first load needs no state change up front.
  useEffect(() => {
    void load()
    return () => {
      latest.current?.abort()
      latest.current = null
    }
  }, [load])

  return { problems, error, refreshing, refresh }
}
