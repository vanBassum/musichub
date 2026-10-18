import { RefreshCw, Upload, X } from "lucide-react"
import { useRef } from "react"

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
import type { FileStatus } from "@/lib/files-api"
import { formatBytes } from "@/lib/format"
import type { FilesState, Upload as UploadItem } from "@/lib/use-files"
import { cn } from "@/lib/utils"

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

export function FilesView({
  state,
  className,
}: {
  state: FilesState
  className?: string
}) {
  const { files, loadError, refreshing, uploads, refresh, start, dismiss } =
    state
  const input = useRef<HTMLInputElement>(null)

  const onSelect = (selected: FileList | null) => {
    if (!selected) return
    Array.from(selected).forEach(start)
    if (input.current) input.current.value = ""
  }

  return (
    <section
      aria-labelledby="files-heading"
      className={cn(
        "flex min-h-0 min-w-0 flex-col overflow-hidden rounded-xl border bg-card",
        className
      )}
    >
      <div className="flex shrink-0 flex-wrap items-center gap-3 p-4">
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
        <ul
          aria-label="Uploads"
          className="flex max-h-[30%] shrink-0 flex-col gap-1.5 overflow-y-auto border-t p-4"
        >
          {uploads.map((upload) => (
            <UploadRow
              key={upload.key}
              upload={upload}
              onDismiss={() => dismiss(upload.key)}
            />
          ))}
        </ul>
      )}

      {loadError && (
        <p role="alert" className="shrink-0 border-t p-4 text-sm text-destructive">
          {loadError}
        </p>
      )}

      <div className="min-h-0 flex-1 overflow-auto border-t [&_[data-slot=table-container]]:overflow-visible">
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

function describeUpload(upload: UploadItem, percent: number) {
  switch (upload.state) {
    case "uploading":
      return {
        label: `Uploading ${percent}%`,
        detail: `Sending ${percent}% (${formatBytes(upload.loaded)} of ${formatBytes(upload.size)}). Not stored yet.`,
      }
    case "sent":
      return {
        label: "Confirming…",
        detail: "All bytes sent. Waiting for the server to confirm storage.",
      }
    case "cancelled":
      return {
        label: "Cancelled",
        detail:
          "Cancelled. Storage was not confirmed; check the list, then upload it again if needed.",
      }
    default:
      return { label: "Failed", detail: `Failed: ${upload.error}` }
  }
}

function UploadRow({
  upload,
  onDismiss,
}: {
  upload: UploadItem
  onDismiss: () => void
}) {
  const percent = upload.size === 0 ? 100 : Math.floor((upload.loaded / upload.size) * 100)
  const active = upload.state === "uploading" || upload.state === "sent"

  const { label, detail } = describeUpload(upload, percent)

  return (
    <li className="flex min-w-0 items-center gap-2 text-sm sm:gap-3">
      <span className="min-w-0 flex-1 truncate" title={upload.name}>
        {upload.name}
      </span>
      {active && (
        <progress
          value={upload.loaded}
          max={upload.size || 1}
          aria-label={`Transfer progress of ${upload.name}`}
          className="hidden h-1.5 w-24 shrink-0 sm:block lg:w-40"
        />
      )}
      <span
        tabIndex={0}
        title={detail}
        role={active ? undefined : "alert"}
        className={cn(
          "shrink-0 rounded-sm text-xs focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none",
          active ? "text-muted-foreground" : "text-destructive"
        )}
      >
        {label}
        <span className="sr-only">. {detail}</span>
      </span>
      {upload.cancel ? (
        <Button
          variant="outline"
          size="sm"
          aria-label={`Cancel upload of ${upload.name}`}
          onClick={upload.cancel}
        >
          Cancel
        </Button>
      ) : (
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label={`Dismiss ${upload.name}`}
          onClick={onDismiss}
        >
          <X />
        </Button>
      )}
    </li>
  )
}
