import { useEffect, useState } from "react"

export interface ExecutionData<T> {
  data: T | null
  error: string | null
}

// Data of one execution (its outcomes, its downloads). It is fetched again when `version` changes (the execution
// moved on), so a running execution keeps up; the previous data stays visible meanwhile. Data of another execution
// is never shown. `fetcher` must be a stable function.
export function useExecutionData<T>(
  fetcher: (executionId: string, signal: AbortSignal) => Promise<T>,
  executionId: string,
  version: string
): ExecutionData<T> {
  const [loaded, setLoaded] = useState<{ id: string; data: T } | null>(null)
  const [failed, setFailed] = useState<{ id: string; message: string } | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    fetcher(executionId, controller.signal).then(
      (data) => {
        if (controller.signal.aborted) return
        setLoaded({ id: executionId, data })
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
  }, [fetcher, executionId, version])

  return {
    data: loaded?.id === executionId ? loaded.data : null,
    error: failed?.id === executionId ? failed.message : null,
  }
}
