import { useCallback, useEffect, useRef, useState } from "react"

export interface LoaderState<T> {
  data: T | null
  error: string | null
  refreshing: boolean
  // Loads again and reports it in `refreshing`.
  refresh: () => Promise<void>
  // Loads again quietly, for polling.
  reload: () => Promise<void>
}

// Loads once and again on demand; only the newest request may write state, starting one aborts the previous. A
// failed load keeps the data that was loaded before, next to the error. `fetcher` must be a stable function.
export function useLoader<T>(
  fetcher: (signal: AbortSignal) => Promise<T>
): LoaderState<T> {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(true)
  const latest = useRef<AbortController | null>(null)

  const reload = useCallback(() => {
    latest.current?.abort()
    const controller = new AbortController()
    latest.current = controller
    return fetcher(controller.signal)
      .then(
        (result) => {
          if (controller.signal.aborted) return
          setData(result)
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
  }, [fetcher])

  const refresh = useCallback(() => {
    setRefreshing(true)
    return reload()
  }, [reload])

  // `refreshing` starts true, so the first load needs no state change up front.
  useEffect(() => {
    void reload()
    return () => {
      latest.current?.abort()
      latest.current = null
    }
  }, [reload])

  return { data, error, refreshing, refresh, reload }
}
