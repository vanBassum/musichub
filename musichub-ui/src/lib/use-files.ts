import { useCallback, useEffect, useRef, useState } from "react"

import { coalesce } from "@/lib/coalesce"
import {
  clampConcurrency,
  createUploadQueue,
  DEFAULT_CONCURRENCY,
  type UploadQueue,
} from "@/lib/upload-queue"

import {
  deleteFailedFiles,
  deleteFile,
  getFileCounts,
  listFiles,
  uploadFile,
  UploadCancelled,
  type FailedFilesDeletion,
  type FileCounts,
  type FilesPage,
  type FilesQuery,
} from "@/lib/files-api"
import { useFilesFilter } from "@/lib/files-filter"

// An upload made in this session. "queued" waits for a free slot; "sent" means every
// byte left the browser but the API has not confirmed storage yet. A confirmed upload
// leaves this list: the list's `Stored` status is the only success.
export type UploadState =
  | "queued"
  | "uploading"
  | "sent"
  | "failed"
  | "cancelled"

// One list refresh per window when uploads finish in a burst.
const REFRESH_WINDOW_MS = 400

export interface Upload {
  key: number
  name: string
  size: number
  loaded: number
  state: UploadState
  error?: string
  cancel?: () => void
}

// The displayed order is the queue's start order: a new upload goes last, so the
// first row of a batch is the first to start.
export function appendUpload(all: Upload[], upload: Upload): Upload[] {
  return [...all, upload]
}

// A delete requested in this session: pending until the API answers; an error stays until retried.
export type DeleteState = { pending: true } | { pending: false; error: string }

// The bulk delete of Failed files: pending, then either the API's counts or an error.
export type BulkDeleteState =
  | { pending: true }
  | { pending: false; result: FailedFilesDeletion }
  | { pending: false; error: string }

export interface FilesState {
  deletions: Record<string, DeleteState>
  bulkDeletion: BulkDeleteState | null
  remove: (id: string) => void
  removeFailed: () => void
  // The page of registrations matching `filter`, and the counts per status and identification state.
  files: FilesPage | null
  counts: FileCounts | null
  filter: FilesQuery
  setFilter: (filter: FilesQuery) => void
  loadError: string | null
  refreshing: boolean
  uploads: Upload[]
  refresh: () => Promise<void>
  start: (file: File) => void
  concurrency: number
  setConcurrency: (limit: number) => void
  // Cancels every queued and running upload; confirmed files are untouched.
  cancelAll: () => void
  dismiss: (key: number) => void
}

// Owns the file list and the uploads. Call it above the views (in `App`), so an
// upload keeps its progress and Cancel control when the user navigates or the
// layout swaps between the desktop and mobile trees.
export function useFiles(): FilesState {
  const { filter, setFilter } = useFilesFilter()
  const [files, setFiles] = useState<FilesPage | null>(null)
  const [counts, setCounts] = useState<FileCounts | null>(null)
  // A failure belongs to the filter it was requested for: it is shown only while that filter is current.
  const [failure, setFailure] = useState<{
    filter: FilesQuery
    message: string
  } | null>(null)
  const loadError = failure?.filter === filter ? failure.message : null
  const [refreshing, setRefreshing] = useState(true)
  const [uploads, setUploads] = useState<Upload[]>([])
  const latest = useRef<AbortController | null>(null)
  const nextKey = useRef(0)

  // `load` reads the filter from a ref so it stays stable: the upload queue below is built from `refresh`.
  const filterRef = useRef(filter)

  // Only the newest request may write state: starting one aborts the previous. The page and the counts load together.
  const load = useCallback(() => {
    latest.current?.abort()
    const controller = new AbortController()
    latest.current = controller
    const requested = filterRef.current
    return Promise.all([
      listFiles(filterRef.current, controller.signal),
      getFileCounts(filterRef.current.search, controller.signal),
    ])
      .then(
        ([list, totals]) => {
          if (controller.signal.aborted) return
          setFiles(list)
          setCounts(totals)
          setFailure(null)
        },
        (error: unknown) => {
          if (controller.signal.aborted) return
          setFailure({
            filter: requested,
            message: error instanceof Error ? error.message : "Loading failed.",
          })
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
  // A new filter loads its page.
  useEffect(() => {
    filterRef.current = filter
    void load()
    return () => {
      latest.current?.abort()
      latest.current = null
    }
  }, [load, filter])

  const patch = useCallback(
    (key: number, change: Partial<Upload>) =>
      setUploads((all) =>
        all.map((u) => (u.key === key ? { ...u, ...change } : u))
      ),
    []
  )

  // Files wait here until the queue gives them a slot.
  const [waiting] = useState(() => new Map<number, File>())
  const [concurrency, setLimit] = useState(DEFAULT_CONCURRENCY)
  const queue = useRef<UploadQueue<number> | null>(null)
  useEffect(() => {
    // A burst of finished uploads costs one list request, not one each.
    const refreshSoon = coalesce(() => void refresh(), REFRESH_WINDOW_MS)
    const created = createUploadQueue<number>({
      limit: DEFAULT_CONCURRENCY,
      start: (key) =>
        uploadFile(waiting.get(key) as File, (loaded, total) =>
          patch(key, { loaded, state: loaded >= total ? "sent" : "uploading" })
        ),
      onStart: (key) => patch(key, { state: "uploading" }),
      onSettled: (key, error) => {
        waiting.delete(key)
        if (!error) {
          // Stored: the list now carries the confirmed registration.
          setUploads((all) => all.filter((u) => u.key !== key))
        } else {
          patch(key, {
            state: error instanceof UploadCancelled ? "cancelled" : "failed",
            error: error.message,
            cancel: undefined,
          })
        }
        refreshSoon()
      },
      // Never started, so nothing can have reached the server: no refresh needed.
      onDequeued: (key) => {
        waiting.delete(key)
        patch(key, {
          state: "cancelled",
          error: "Upload cancelled.",
          cancel: undefined,
        })
      },
    })
    queue.current = created
    return () => {
      refreshSoon.cancel()
      queue.current = null
    }
  }, [patch, refresh, waiting])

  const start = useCallback(
    (file: File) => {
      const key = nextKey.current++
      waiting.set(key, file)
      setUploads((all) =>
        appendUpload(all, {
          key,
          name: file.name,
          size: file.size,
          loaded: 0,
          state: "queued",
          cancel: () => queue.current?.cancel(key),
        })
      )
      queue.current?.enqueue(key)
    },
    [waiting]
  )

  const setConcurrency = useCallback((limit: number) => {
    const next = clampConcurrency(limit)
    setLimit(next)
    queue.current?.setLimit(next)
  }, [])

  const cancelAll = useCallback(() => queue.current?.cancelAll(), [])

  const [deletions, setDeletions] = useState<Record<string, DeleteState>>({})
  const inFlight = useRef(new Set<string>())

  // One request per file at a time: a double click or a second confirm is ignored.
  const remove = useCallback(
    (id: string) => {
      if (inFlight.current.has(id)) return
      inFlight.current.add(id)
      setDeletions((all) => ({ ...all, [id]: { pending: true } }))
      deleteFile(id)
        .then(
          () =>
            setDeletions((all) => {
              const rest = { ...all }
              delete rest[id]
              return rest
            }),
          (error: Error) =>
            setDeletions((all) => ({
              ...all,
              [id]: { pending: false, error: error.message },
            }))
        )
        .finally(() => {
          inFlight.current.delete(id)
          void refresh()
        })
    },
    [refresh]
  )

  const [bulkDeletion, setBulkDeletion] = useState<BulkDeleteState | null>(null)
  const bulkInFlight = useRef(false)

  // One bulk delete at a time. The API selects the Failed files when it runs; the list is refreshed afterwards.
  const removeFailed = useCallback(() => {
    if (bulkInFlight.current) return
    bulkInFlight.current = true
    setBulkDeletion({ pending: true })
    deleteFailedFiles()
      .then(
        (result) => setBulkDeletion({ pending: false, result }),
        (error: Error) =>
          setBulkDeletion({ pending: false, error: error.message })
      )
      .finally(() => {
        bulkInFlight.current = false
        void refresh()
      })
  }, [refresh])

  const dismiss = useCallback(
    (key: number) => setUploads((all) => all.filter((u) => u.key !== key)),
    []
  )

  return {
    files,
    counts,
    filter,
    setFilter,
    loadError,
    refreshing,
    uploads,
    deletions,
    bulkDeletion,
    refresh,
    start,
    concurrency,
    setConcurrency,
    cancelAll,
    dismiss,
    remove,
    removeFailed,
  }
}
