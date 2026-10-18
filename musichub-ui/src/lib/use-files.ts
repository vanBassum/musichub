import { useCallback, useEffect, useRef, useState } from "react"

import {
  listFiles,
  uploadFile,
  UploadCancelled,
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

export interface FilesState {
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

  const dismiss = useCallback(
    (key: number) => setUploads((all) => all.filter((u) => u.key !== key)),
    []
  )

  return { files, loadError, refreshing, uploads, refresh, start, dismiss }
}
