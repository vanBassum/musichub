import { useCallback, useEffect, useRef, useState } from "react"

import {
  getExecution,
  listExecutions,
  listJobs,
  startCatalogReconciliation,
  type JobExecution,
  type JobExecutionDetails,
  type JobTask,
} from "@/lib/jobs-api"

const pollMs = 3000

export interface JobsState {
  tasks: JobTask[] | null
  executions: JobExecution[] | null
  loadError: string | null
  refreshing: boolean
  // The execution whose details show: the user's pick, else the newest.
  selectedId: string | null
  select: (id: string) => void
  details: JobExecutionDetails | null
  detailsError: string | null
  starting: boolean
  startError: string | null
  refresh: () => Promise<void>
  start: () => void
}

// Owns the task list, the executions and the selected execution. Call it above the views (in `App`), so the
// selection and a start in flight survive navigation and the desktop/mobile layout swap. While a run is waiting
// or in progress the data is polled.
export function useJobs(): JobsState {
  const [tasks, setTasks] = useState<JobTask[] | null>(null)
  const [executions, setExecutions] = useState<JobExecution[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(true)
  const [picked, setPicked] = useState<string | null>(null)
  const [loaded, setLoaded] = useState<JobExecutionDetails | null>(null)
  const [detailsError, setDetailsError] = useState<string | null>(null)
  const [starting, setStarting] = useState(false)
  const [startError, setStartError] = useState<string | null>(null)
  const latest = useRef<AbortController | null>(null)
  const starts = useRef(false)

  const selectedId = picked ?? executions?.[0]?.id ?? null
  // Details of another execution are not shown while the new ones load.
  const details = loaded?.execution.id === selectedId ? loaded : null

  // Only the newest request may write state: starting one aborts the previous.
  const load = useCallback(() => {
    latest.current?.abort()
    const controller = new AbortController()
    latest.current = controller
    return Promise.all([
      listJobs(controller.signal),
      listExecutions(controller.signal),
    ])
      .then(
        ([jobs, runs]) => {
          if (controller.signal.aborted) return
          setTasks(jobs)
          setExecutions(runs)
          setLoadError(null)
        },
        (error: unknown) => {
          if (controller.signal.aborted) return
          setLoadError(error instanceof Error ? error.message : "Loading failed.")
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

  const active = tasks?.some((task) => task.activeExecutionId !== null) ?? false
  useEffect(() => {
    if (!active) return
    const timer = setInterval(() => void load(), pollMs)
    return () => clearInterval(timer)
  }, [active, load])

  // The details are fetched again whenever the list changes, so a running execution keeps up.
  useEffect(() => {
    if (selectedId === null) return
    const controller = new AbortController()
    getExecution(selectedId, controller.signal).then(
      (result) => {
        if (controller.signal.aborted) return
        setLoaded(result)
        setDetailsError(null)
      },
      (error: unknown) => {
        if (controller.signal.aborted) return
        setDetailsError(
          error instanceof Error ? error.message : "Loading failed."
        )
      }
    )
    return () => controller.abort()
  }, [selectedId, executions])

  // One start at a time: a double click is ignored. The new run is selected once the list has it.
  const start = useCallback(() => {
    if (starts.current) return
    starts.current = true
    setStarting(true)
    setStartError(null)
    startCatalogReconciliation()
      .then(
        (id) => setPicked(id),
        (error: Error) => setStartError(error.message)
      )
      .finally(() => {
        starts.current = false
        setStarting(false)
        void refresh()
      })
  }, [refresh])

  return {
    tasks,
    executions,
    loadError,
    refreshing,
    selectedId,
    select: setPicked,
    details,
    detailsError,
    starting,
    startError,
    refresh,
    start,
  }
}
