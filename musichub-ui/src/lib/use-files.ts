import { useCallback, useEffect, useRef, useState } from "react"

import {
  deleteFailedFiles,
  deleteFile,
  listFiles,
  uploadFile,
  UploadCancelled,
  type FailedFilesDeletion,
  type FileRegistration,
} from "@/lib/files-api"

// An upload made in this session. "sent" means every byte left the browser but the
// API has not confirmed storage yet; only the list's `Stored` status is success.
export type UploadState = "uploading" | "sent" | "failed" | "cancelled"

export interface Upload {
  key: number
  name: string
  size: number
  loaded: number
  state: UploadState
  error?: string
  cancel?: () => void
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
  files: FileRegistration[] | null
  loadError: string | null
  refreshing: boolean
  uploads: Upload[]
  refresh: () => Promise<void>
  start: (file: File) => void
  dismiss: (key: number) => void
}

// Owns the file list and the uploads. Call it above the views (in `App`), so an
// upload keeps its progress and Cancel control when the user navigates or the
// layout swaps between the desktop and mobile trees.
export function useFiles(): FilesState {
  const [files, setFiles] = useState<FileRegistration[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(true)
  const [uploads, setUploads] = useState<Upload[]>([])
  const latest = useRef<AbortController | null>(null)
  const nextKey = useRef(0)

  // Only the newest request may write state: starting one aborts the previous.
  const load = useCallback(() => {
    latest.current?.abort()
    const controller = new AbortController()
    latest.current = controller
    return listFiles(controller.signal)
      .then(
        (list) => {
          if (controller.signal.aborted) return
          setFiles(list)
          setLoadError(null)
        },
        (error: unknown) => {
          if (controller.signal.aborted) return
          setLoadError(
            error instanceof Error ? error.message : "Loading failed."
          )
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

  const patch = useCallback(
    (key: number, change: Partial<Upload>) =>
      setUploads((all) =>
        all.map((u) => (u.key === key ? { ...u, ...change } : u))
      ),
    []
  )

  const start = useCallback(
    (file: File) => {
      const key = nextKey.current++
      const handle = uploadFile(file, (loaded, total) =>
        patch(key, { loaded, state: loaded >= total ? "sent" : "uploading" })
      )
      setUploads((all) => [
        {
          key,
          name: file.name,
          size: file.size,
          loaded: 0,
          state: "uploading",
          cancel: handle.cancel,
        },
        ...all,
      ])
      handle.result.then(
        () => {
          // Stored: the list now carries the confirmed registration.
          setUploads((all) => all.filter((u) => u.key !== key))
          void refresh()
        },
        (error: Error) => {
          patch(key, {
            state: error instanceof UploadCancelled ? "cancelled" : "failed",
            error: error.message,
            cancel: undefined,
          })
          void refresh()
        }
      )
    },
    [patch, refresh]
  )

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
    loadError,
    refreshing,
    uploads,
    deletions,
    bulkDeletion,
    refresh,
    start,
    dismiss,
    remove,
    removeFailed,
  }
}
