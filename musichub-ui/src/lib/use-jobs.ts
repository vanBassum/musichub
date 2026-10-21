import { useCallback, useEffect, useRef, useState } from "react"

import {
  getExecution,
  listExecutions,
  listJobs,
  startJob,
  type JobExecution,
  type JobExecutionDetails,
  type JobTask,
} from "@/lib/jobs-api"
import { useLoader } from "@/lib/use-loader"

const pollMs = 3000

function fetchLists(signal: AbortSignal) {
  return Promise.all([listJobs(signal), listExecutions(signal)]).then(
    ([tasks, executions]) => ({ tasks, executions })
  )
}

export type JobsTab = "tasks" | "history"

export interface JobsState {
  tab: JobsTab
  setTab: (tab: JobsTab) => void
  tasks: JobTask[] | null
  executions: JobExecution[] | null
  loadError: string | null
  refreshing: boolean
  // The execution whose details show: the user's pick, else the newest.
  selectedId: string | null
  // Whether the user opened a run: the stacked mobile History then shows its details instead of the list.
  detailOpen: boolean
  select: (id: string) => void
  closeDetails: () => void
  details: JobExecutionDetails | null
  // Counts the loads of the details, so what hangs off a running execution can load again with it.
  detailsRevision: number
  detailsError: string | null
  // The task whose start is in flight.
  starting: string | null
  startError: string | null
  refresh: () => Promise<void>
  start: (taskId: string) => void
}

// Owns the task list, the executions and the selected execution. Call it above the views (in `App`), so the
// selection and a start in flight survive navigation and the desktop/mobile layout swap. While a run is waiting
// or in progress the data is polled.
export function useJobs(): JobsState {
  const {
    data: lists,
    error: loadError,
    refreshing,
    refresh,
    reload,
  } = useLoader(fetchLists)
  const [tab, setTab] = useState<JobsTab>("tasks")
  const [picked, setPicked] = useState<string | null>(null)
  const [loaded, setLoaded] = useState<JobExecutionDetails | null>(null)
  const [detailsRevision, setDetailsRevision] = useState(0)
  const [failedLoad, setFailedLoad] = useState<{ id: string; message: string } | null>(null)
  const [starting, setStarting] = useState<string | null>(null)
  const [startError, setStartError] = useState<string | null>(null)
  const starts = useRef(false)

  const tasks = lists?.tasks ?? null
  const executions = lists?.executions ?? null
  const selectedId = picked ?? executions?.[0]?.id ?? null
  // Details of another execution are not shown while the new ones load.
  const details = loaded?.execution.id === selectedId ? loaded : null
  const detailsError = failedLoad?.id === selectedId ? failedLoad.message : null

  const active = tasks?.some((task) => task.activeExecutionId !== null) ?? false
  useEffect(() => {
    if (!active) return
    const timer = setInterval(() => void reload(), pollMs)
    return () => clearInterval(timer)
  }, [active, reload])

  // The details are fetched again whenever the list changes, so a running execution keeps up.
  useEffect(() => {
    if (selectedId === null) return
    const controller = new AbortController()
    getExecution(selectedId, controller.signal).then(
      (result) => {
        if (controller.signal.aborted) return
        setLoaded(result)
        setDetailsRevision((revision) => revision + 1)
        setFailedLoad(null)
      },
      (error: unknown) => {
        if (controller.signal.aborted) return
        setFailedLoad({
          id: selectedId,
          message: error instanceof Error ? error.message : "Loading failed.",
        })
      }
    )
    return () => controller.abort()
  }, [selectedId, executions])

  // One start at a time: a double click is ignored. The new run is selected once the list has it.
  const start = useCallback(
    (taskId: string) => {
      if (starts.current) return
      starts.current = true
      setStarting(taskId)
      setStartError(null)
      startJob(taskId)
        .then(
          (id) => setPicked(id),
          (error: Error) => setStartError(error.message)
        )
        .finally(() => {
          starts.current = false
          setStarting(null)
          void refresh()
        })
    },
    [refresh]
  )

  // Picking a run, also from another page, shows it in History.
  const select = useCallback((id: string) => {
    setPicked(id)
    setTab("history")
  }, [])
  const closeDetails = useCallback(() => setPicked(null), [])

  return {
    tab,
    setTab,
    tasks,
    executions,
    loadError,
    refreshing,
    selectedId,
    detailOpen: picked !== null,
    select,
    closeDetails,
    details,
    detailsRevision,
    detailsError,
    starting,
    startError,
    refresh,
    start,
  }
}
