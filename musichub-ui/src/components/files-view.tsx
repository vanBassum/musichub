import {
  ChevronRight,
  MoreHorizontal,
  Pause,
  Play,
  RefreshCw,
  Trash2,
  Upload,
  X,
} from "lucide-react"
import { Fragment, useRef, useState } from "react"

import {
  FileIdentificationDetails,
  IdentificationBadge,
} from "@/components/file-identification"
import { FilesFilters, FilesResultBar } from "@/components/files-filters"
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
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  isAudioFilename,
  type FileRegistration,
  type FileStatus,
} from "@/lib/files-api"
import { isFilesFiltered } from "@/lib/files-filter"
import { formatBytes } from "@/lib/format"
import { MAX_CONCURRENCY, MIN_CONCURRENCY } from "@/lib/upload-queue"
import type {
  BulkDeleteState,
  DeleteState,
  FilesState,
  Upload as UploadItem,
} from "@/lib/use-files"
import { isAudible, isLoaded, usePlayer } from "@/lib/use-player"
import { cn } from "@/lib/utils"

const statusClassNames: Record<FileStatus, string> = {
  Stored: "bg-success text-success-foreground",
  Failed: "bg-destructive text-white",
  Uploading: "bg-primary text-primary-foreground",
  Pending: "bg-warning text-warning-foreground",
  Expired: "bg-muted text-muted-foreground",
  Deleting: "bg-warning text-warning-foreground",
}

const concurrencyOptions = Array.from(
  { length: MAX_CONCURRENCY - MIN_CONCURRENCY + 1 },
  (_, i) => MIN_CONCURRENCY + i
)

const statusHints: Record<FileStatus, string> = {
  Stored: "Storage confirmed",
  Failed: "Not stored: upload it again",
  Uploading: "Upload in progress or interrupted: not confirmed as stored",
  Pending: "Registered, not confirmed as stored",
  Expired: "Never finished and not stored: upload it again",
  Deleting: "Deletion not finished: delete it again to retry",
}

function fileSize(bytes: number | null) {
  return bytes === null ? "Unknown" : formatBytes(bytes)
}

// The one mapping from delete state to what a row shows: used by the actions cell and the full-width row below sm.
function deleteStatusKind(state: DeleteState | undefined, confirming: boolean) {
  if (state?.pending) return "pending"
  if (confirming) return "confirm"
  return state ? "error" : null
}

export function FilesView({
  state,
  className,
}: {
  state: FilesState
  className?: string
}) {
  const {
    files: page,
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
  } = state
  const unfinished = uploads.filter((u) => u.cancel).length
  const input = useRef<HTMLInputElement>(null)
  const [confirming, setConfirming] = useState<string | null>(null)
  const [confirmingBulk, setConfirmingBulk] = useState(false)
  const [expanded, setExpanded] = useState<string | null>(null)
  const files = page?.items
  // Every failed file, not only those on this page or matching the filters.
  const failedCount = counts?.statuses.Failed ?? 0
  const filtered = isFilesFiltered(filter)

  const onSelect = (selected: FileList | null) => {
    if (!selected) return
    Array.from(selected).forEach(start)
    if (input.current) input.current.value = ""
  }

  const deleteProps = (file: FileRegistration) => ({
    name: file.originalFilename,
    state: deletions[file.id],
    confirming: confirming === file.id,
    onAsk: () => setConfirming(file.id),
    onCancel: () => setConfirming(null),
    onConfirm: () => {
      setConfirming(null)
      remove(file.id)
    },
  })

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
        <div className="ml-auto flex flex-wrap items-center gap-2">
          {unfinished > 0 && (
            <Button variant="outline" size="lg" onClick={cancelAll}>
              Cancel all uploads
            </Button>
          )}
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
          <FilesMenu
            refreshing={refreshing}
            onRefresh={() => void refresh()}
            concurrency={concurrency}
            onConcurrency={setConcurrency}
            failedCount={failedCount}
            bulkPending={bulkDeletion?.pending === true}
            onDeleteFailed={() => setConfirmingBulk(true)}
          />
        </div>
      </div>

      <FilesFilters filter={filter} counts={counts} onChange={setFilter} />

      {confirmingBulk && failedCount > 0 && !bulkDeletion?.pending && (
        <ConfirmDeleteFailed
          count={failedCount}
          onCancel={() => setConfirmingBulk(false)}
          onConfirm={() => {
            setConfirmingBulk(false)
            removeFailed()
          }}
        />
      )}

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

      {bulkDeletion && !bulkDeletion.pending && (
        <BulkDeleteResult state={bulkDeletion} />
      )}

      {loadError && (
        <p role="alert" className="shrink-0 border-t p-4 text-sm text-destructive">
          {loadError}
        </p>
      )}

      <FilesResultBar filter={filter} page={page} onChange={setFilter} />

      <div className="min-h-0 flex-1 overflow-auto border-t [&_[data-slot=table-container]]:overflow-visible">
        <Table className="table-fixed text-sm sm:table-auto">
          <TableHeader className="sticky top-0 z-10 bg-card">
            <TableRow className="hover:bg-transparent">
              <TableHead className="pl-4">Filename</TableHead>
              <TableHead className="hidden sm:table-cell">Size</TableHead>
              <TableHead className="w-24 sm:w-auto">Status</TableHead>
              <TableHead className="hidden md:table-cell">Identification</TableHead>
              <TableHead className="hidden sm:table-cell">Started</TableHead>
              <TableHead className="w-14 pr-4 sm:w-auto">
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {files?.map((file) => (
              <Fragment key={file.id}>
                <TableRow>
                  <TableCell className="truncate pl-4 sm:max-w-80" title={file.originalFilename}>
                    {file.originalFilename}
                    <span className="block truncate text-xs text-muted-foreground sm:hidden">
                      {fileSize(file.sizeBytes)} · {new Date(file.startedAt).toLocaleString()}
                    </span>
                    {file.identification && (
                      <span className="mt-1 block md:hidden">
                        <IdentificationBadge identification={file.identification} />
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="hidden sm:table-cell">
                    {fileSize(file.sizeBytes)}
                  </TableCell>
                  <TableCell>
                    <Badge
                      title={
                        file.deletionError ??
                        file.statusDetail ??
                        statusHints[file.status]
                      }
                      className={statusClassNames[file.status]}
                    >
                      {file.status}
                    </Badge>
                  </TableCell>
                  <TableCell className="hidden md:table-cell">
                    {file.identification && (
                      <IdentificationBadge identification={file.identification} />
                    )}
                  </TableCell>
                  <TableCell className="hidden sm:table-cell">
                    {new Date(file.startedAt).toLocaleString()}
                  </TableCell>
                  <TableCell className="pr-4">
                    <span className="flex items-center justify-end gap-1">
                      {file.identification && (
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-expanded={expanded === file.id}
                          aria-label={`Identification details of ${file.originalFilename}`}
                          onClick={() =>
                            setExpanded(expanded === file.id ? null : file.id)
                          }
                        >
                          <ChevronRight
                            className={cn(
                              "transition-transform",
                              expanded === file.id && "rotate-90"
                            )}
                          />
                        </Button>
                      )}
                      {file.status === "Stored" &&
                        isAudioFilename(file.originalFilename) && (
                          <PlayControl file={file} />
                        )}
                      <DeleteControl
                        {...deleteProps(file)}
                        retry={file.status === "Deleting"}
                      />
                    </span>
                  </TableCell>
                </TableRow>
                {expanded === file.id && file.identification && (
                  <TableRow className="hover:bg-transparent">
                    <TableCell colSpan={6} className="px-4 whitespace-normal">
                      <FileIdentificationDetails
                        fileId={file.id}
                        identification={file.identification}
                      />
                    </TableCell>
                  </TableRow>
                )}
                {deleteStatusKind(deletions[file.id], confirming === file.id) && (
                  <TableRow className="sm:hidden">
                    <TableCell colSpan={3} className="px-4 whitespace-normal">
                      <DeleteStatus {...deleteProps(file)} className="flex" />
                    </TableCell>
                  </TableRow>
                )}
              </Fragment>
            ))}
            {files?.length === 0 && (
              <TableRow className="hover:bg-transparent">
                <TableCell colSpan={6} className="p-6 text-center text-muted-foreground">
                  {filtered
                    ? "No files match the filters."
                    : "No files yet. Upload one to see it here."}
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>
    </section>
  )
}

// Plays a Stored audio file in the shared player; the row of the loaded file toggles pause.
function PlayControl({ file }: { file: FileRegistration }) {
  const { source, status, playFile, toggle } = usePlayer()
  const current = isLoaded(source, status, file.id)
  const playing = current && isAudible(status)
  return (
    <Button
      variant="ghost"
      size="icon-sm"
      aria-label={`${playing ? "Pause" : "Play"} ${file.originalFilename}`}
      onClick={() => (current ? toggle() : playFile(file))}
    >
      {playing ? <Pause /> : <Play />}
    </Button>
  )
}

// The less-used controls: refresh, the simultaneous-uploads setting and the bulk delete of Failed files.
// The count is what the list shows now; the API decides again when the delete runs.
function FilesMenu({
  refreshing,
  onRefresh,
  concurrency,
  onConcurrency,
  failedCount,
  bulkPending,
  onDeleteFailed,
}: {
  refreshing: boolean
  onRefresh: () => void
  concurrency: number
  onConcurrency: (limit: number) => void
  failedCount: number
  bulkPending: boolean
  onDeleteFailed: () => void
}) {
  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button variant="outline" size="icon-lg" aria-label="More file actions" />
        }
      >
        <MoreHorizontal />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem disabled={refreshing} onClick={onRefresh}>
          <RefreshCw />
          Refresh
        </DropdownMenuItem>
        <DropdownMenuSub>
          <DropdownMenuSubTrigger>Simultaneous uploads ({concurrency})</DropdownMenuSubTrigger>
          <DropdownMenuSubContent>
            <DropdownMenuRadioGroup
              value={String(concurrency)}
              onValueChange={(value) => onConcurrency(Number(value))}
            >
              {concurrencyOptions.map((n) => (
                <DropdownMenuRadioItem key={n} value={String(n)}>
                  {n}
                </DropdownMenuRadioItem>
              ))}
            </DropdownMenuRadioGroup>
          </DropdownMenuSubContent>
        </DropdownMenuSub>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          variant="destructive"
          disabled={failedCount === 0 || bulkPending}
          onClick={onDeleteFailed}
        >
          <Trash2 />
          {bulkPending ? "Deleting failed files…" : "Delete all failed files"}
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

function ConfirmDeleteFailed({
  count,
  onCancel,
  onConfirm,
}: {
  count: number
  onCancel: () => void
  onConfirm: () => void
}) {
  return (
    <div
      role="group"
      aria-label="Confirm deleting all failed files"
      className="flex shrink-0 flex-wrap items-center gap-1.5 border-t px-4 py-2"
    >
      <span className="text-sm">
        Delete {count} failed {count === 1 ? "file" : "files"}? This cannot be undone.
      </span>
      <Button variant="destructive" size="lg" onClick={onConfirm}>
        Delete
      </Button>
      <Button variant="outline" size="lg" onClick={onCancel}>
        Keep
      </Button>
    </div>
  )
}

function BulkDeleteResult({
  state,
}: {
  state: Exclude<BulkDeleteState, { pending: true }>
}) {
  if ("error" in state) {
    return (
      <p role="alert" className="shrink-0 border-t p-4 text-sm text-destructive">
        {state.error}
      </p>
    )
  }
  const { deleted, skipped, failed } = state.result
  const problems = failed.length > 0
  return (
    <div
      role={problems ? "alert" : "status"}
      className={cn(
        "shrink-0 border-t p-4 text-sm",
        problems ? "text-destructive" : "text-muted-foreground"
      )}
    >
      <p>
        Deleted {deleted} failed {deleted === 1 ? "file" : "files"}
        {skipped > 0 && `; ${skipped} skipped because they changed meanwhile`}
        {problems && `; ${failed.length} could not be deleted`}.
      </p>
      {problems && (
        <>
          <ul className="mt-1 list-disc pl-5">
            {failed.map((f) => (
              <li key={f.fileId}>
                File {f.fileId}: {f.detail}
              </li>
            ))}
          </ul>
          <p className="mt-1">
            They stay in the list as Deleting: delete each one again to retry.
          </p>
        </>
      )}
    </div>
  )
}

function ConfirmDelete({
  name,
  onCancel,
  onConfirm,
}: {
  name: string
  onCancel: () => void
  onConfirm: () => void
}) {
  return (
    <span
      role="group"
      aria-label={`Confirm deleting ${name}`}
      className="flex flex-wrap items-center gap-1.5"
    >
      <span className="text-xs">Delete the stored file?</span>
      <Button
        variant="destructive"
        size="sm"
        aria-label={`Confirm delete of ${name}`}
        onClick={onConfirm}
      >
        Delete
      </Button>
      <Button variant="outline" size="sm" onClick={onCancel}>
        Keep
      </Button>
    </span>
  )
}

// Renders the pending, confirming or error state of a delete; nothing when there is none.
function DeleteStatus({
  name,
  state,
  confirming,
  onCancel,
  onConfirm,
  className,
}: {
  name: string
  state: DeleteState | undefined
  confirming: boolean
  onCancel: () => void
  onConfirm: () => void
  className: string
}) {
  const kind = deleteStatusKind(state, confirming)
  if (!kind) return null
  return (
    <span className={cn("items-center", className)}>
      {kind === "pending" ? (
        <span role="status" className="text-xs text-muted-foreground">
          Deleting…
        </span>
      ) : kind === "confirm" ? (
        <ConfirmDelete name={name} onCancel={onCancel} onConfirm={onConfirm} />
      ) : (
        <span role="alert" className="text-xs text-destructive">
          {state && !state.pending ? state.error : null}
        </span>
      )}
    </span>
  )
}

// Delete with an inline confirmation. On success the row leaves the list (the list is refreshed); a failure shows here and the row stays.
// Below sm the actions column is too narrow for the status: the row shows it in a full-width row under the file instead.
function DeleteControl({
  retry,
  onAsk,
  ...status
}: {
  name: string
  state: DeleteState | undefined
  retry: boolean
  confirming: boolean
  onAsk: () => void
  onCancel: () => void
  onConfirm: () => void
}) {
  const kind = deleteStatusKind(status.state, status.confirming)
  return (
    <span className="flex items-center gap-2">
      <DeleteStatus {...status} className="hidden sm:flex" />
      {kind !== "pending" && kind !== "confirm" && (
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label={`${retry || status.state ? "Retry delete of" : "Delete"} ${status.name}`}
          onClick={onAsk}
        >
          <Trash2 />
        </Button>
      )}
    </span>
  )
}

function describeUpload(upload: UploadItem, percent: number) {
  switch (upload.state) {
    case "queued":
      return {
        label: "Queued",
        detail: "Waiting for a free upload slot. Nothing has been sent yet.",
      }
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
  const active = upload.cancel !== undefined
  const transferring = upload.state === "uploading" || upload.state === "sent"

  const { label, detail } = describeUpload(upload, percent)

  return (
    <li className="flex min-w-0 items-center gap-2 text-sm sm:gap-3">
      <span className="min-w-0 flex-1 truncate" title={upload.name}>
        {upload.name}
      </span>
      {transferring && (
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
