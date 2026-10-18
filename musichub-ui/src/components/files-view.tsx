import { RefreshCw, Upload, X } from "lucide-react"
import { useCallback, useEffect, useRef, useState } from "react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import {
  listFiles,
  uploadFile,
  type FileRegistration,
  type FileStatus,
} from "@/lib/files-api"
import { formatBytes } from "@/lib/format"
import { cn } from "@/lib/utils"

// An upload made in this session. "sent" means every byte left the browser but the
// API has not confirmed storage yet; only the list's `Stored` status is success.
type UploadState = "uploading" | "sent" | "failed" | "cancelled"

interface Upload {
  key: number
  name: string
  size: number
  loaded: number
  state: UploadState
  error?: string
  cancel?: () => void
}

const statusClassNames: Record<FileStatus, string> = {
  Stored: "bg-success text-success-foreground",
  Failed: "bg-destructive text-white",
  Uploading: "bg-primary text-primary-foreground",
  Pending: "bg-warning text-warning-foreground",
}

const statusHints: Record<FileStatus, string> = {
  Stored: "Storage confirmed",
  Failed: "Not stored: upload it again",
  Uploading: "Upload in progress or interrupted: not confirmed as stored",
  Pending: "Registered, not confirmed as stored",
}

export function FilesView({ className }: { className?: string }) {
  const [files, setFiles] = useState<FileRegistration[] | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(true)
  const [uploads, setUploads] = useState<Upload[]>([])
  const input = useRef<HTMLInputElement>(null)
  const nextKey = useRef(0)

  const refresh = useCallback(async () => {
    setRefreshing(true)
    try {
      setFiles(await listFiles())
      setLoadError(null)
    } catch (error) {
      setLoadError(error instanceof Error ? error.message : "Loading failed.")
    } finally {
      setRefreshing(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const patch = (key: number, change: Partial<Upload>) =>
    setUploads((all) => all.map((u) => (u.key === key ? { ...u, ...change } : u)))

  const start = (file: File) => {
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
        const cancelled = error.message === "Upload cancelled."
        patch(key, {
          state: cancelled ? "cancelled" : "failed",
          error: error.message,
          cancel: undefined,
        })
        void refresh()
      }
    )
  }

  const onSelect = (selected: FileList | null) => {
    if (!selected) return
    Array.from(selected).forEach(start)
    if (input.current) input.current.value = ""
  }

  return (
    <section
      aria-labelledby="files-heading"
      className={cn(
        "flex min-h-0 min-w-0 flex-col rounded-xl border bg-card",
        className
      )}
    >
      <div className="flex flex-wrap items-center gap-3 p-4">
        <h1 id="files-heading" className="text-xl font-semibold">
          Files
        </h1>
        <span className="text-muted-foreground">
          Uploaded files as stored, not the music catalog
        </span>
        <div className="ml-auto flex items-center gap-2">
          <Button
            variant="outline"
            size="lg"
            onClick={() => void refresh()}
            disabled={refreshing}
          >
            <RefreshCw data-icon="inline-start" />
            Refresh
          </Button>
          <Button size="lg" onClick={() => input.current?.click()}>
            <Upload data-icon="inline-start" />
            Upload files
          </Button>
          <input
            ref={input}
            type="file"
            multiple
            hidden
            aria-label="Choose files to upload"
            onChange={(event) => onSelect(event.target.files)}
          />
        </div>
      </div>

      {uploads.length > 0 && (
        <ul aria-label="Uploads" className="flex flex-col gap-2 border-t p-4">
          {uploads.map((upload) => (
            <UploadRow
              key={upload.key}
              upload={upload}
              onDismiss={() =>
                setUploads((all) => all.filter((u) => u.key !== upload.key))
              }
            />
          ))}
        </ul>
      )}

      {loadError && (
        <p role="alert" className="border-t p-4 text-sm text-destructive">
          {loadError}
        </p>
      )}

      <div className="min-h-0 flex-1 overflow-auto border-t">
        <Table className="min-w-[36rem] text-sm">
          <TableHeader className="sticky top-0 z-10 bg-card">
            <TableRow className="hover:bg-transparent">
              <TableHead className="pl-4">Filename</TableHead>
              <TableHead>Size</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="pr-4">Started</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {files?.map((file) => (
              <TableRow key={file.id}>
                <TableCell className="max-w-80 truncate pl-4" title={file.originalFilename}>
                  {file.originalFilename}
                </TableCell>
                <TableCell>
                  {file.sizeBytes === null ? "Unknown" : formatBytes(file.sizeBytes)}
                </TableCell>
                <TableCell>
                  <Badge
                    title={statusHints[file.status]}
                    className={statusClassNames[file.status]}
                  >
                    {file.status}
                  </Badge>
                </TableCell>
                <TableCell className="pr-4">
                  {new Date(file.startedAt).toLocaleString()}
                </TableCell>
              </TableRow>
            ))}
            {files?.length === 0 && (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={4} className="p-6 text-center text-muted-foreground">
                  No files yet. Upload one to see it here.
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>
    </section>
  )
}

function UploadRow({
  upload,
  onDismiss,
}: {
  upload: Upload
  onDismiss: () => void
}) {
  const percent = upload.size === 0 ? 100 : Math.floor((upload.loaded / upload.size) * 100)
  const active = upload.state === "uploading" || upload.state === "sent"

  return (
    <li className="flex items-center gap-3 text-sm">
      <div className="min-w-0 flex-1">
        <div className="truncate" title={upload.name}>
          {upload.name}
        </div>
        {active ? (
          <>
            <progress
              value={upload.loaded}
              max={upload.size || 1}
              aria-label={`Transfer progress of ${upload.name}`}
              className="h-1.5 w-full"
            />
            <div className="text-xs text-muted-foreground">
              {upload.state === "sent"
                ? "All bytes sent. Waiting for the server to confirm storage…"
                : `Sending ${percent}% (${formatBytes(upload.loaded)} of ${formatBytes(upload.size)}). Not stored yet.`}
            </div>
          </>
        ) : (
          <div role="alert" className="text-xs text-destructive">
            {upload.state === "cancelled"
              ? "Cancelled. Storage was not confirmed; check the list, then upload it again if needed."
              : `Failed: ${upload.error}`}
          </div>
        )}
      </div>
      {upload.cancel ? (
        <Button variant="outline" size="sm" onClick={upload.cancel}>
          Cancel
        </Button>
      ) : (
        <Button variant="ghost" size="icon-sm" aria-label="Dismiss" onClick={onDismiss}>
          <X />
        </Button>
      )}
    </li>
  )
}
