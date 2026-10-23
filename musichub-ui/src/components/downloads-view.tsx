import {
  CheckCircle2,
  ChevronRight,
  Clock,
  Loader2,
  RefreshCw,
  RotateCw,
  X,
  XCircle,
  type LucideIcon,
} from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import { Skeleton } from "@/components/ui/skeleton"
import {
  canRemoveDownload,
  canRetryDownload,
  downloadErrorHint,
  downloadFileName,
  downloadStatuses,
  isDownloadActive,
  type DownloadQueueItem,
  type DownloadQueueStatus,
  type DownloadStatusFilter,
} from "@/lib/downloads-api"
import type { DownloadQueueState } from "@/lib/use-download-queue"
import { cn } from "@/lib/utils"

const statusLabels: Record<DownloadQueueStatus, string> = {
  Queued: "Queued",
  Downloading: "Downloading",
  Downloaded: "Downloaded, importing",
  Completed: "Completed",
  Failed: "Failed",
}

const statusIcons: Record<DownloadQueueStatus, LucideIcon> = {
  Queued: Clock,
  Downloading: Loader2,
  Downloaded: Loader2,
  Completed: CheckCircle2,
  Failed: XCircle,
}

const statusTextClassNames: Record<DownloadQueueStatus, string> = {
  Queued: "text-muted-foreground",
  Downloading: "text-primary",
  Downloaded: "text-primary",
  Completed: "text-success",
  Failed: "text-destructive",
}

const formatTime = (iso: string) => new Date(iso).toLocaleString()

// Status as icon and text, plus what is relevant for it: a short error hint for a failure, progress for an
// active transfer. Completed and queued requests show nothing more.
function StatusCell({ item }: { item: DownloadQueueItem }) {
  const Icon = statusIcons[item.status]
  return (
    <div className="flex min-w-0 items-center gap-2 text-sm md:w-72">
      <span
        className={cn(
          "flex shrink-0 items-center gap-1.5 font-medium",
          statusTextClassNames[item.status]
        )}
      >
        <Icon aria-hidden className="size-4" />
        {statusLabels[item.status]}
      </span>
      {item.status === "Failed" && item.error !== null && (
        <span className="truncate text-muted-foreground" title={item.error}>
          {downloadErrorHint(item.error)}
        </span>
      )}
      {item.status === "Downloading" && item.progressPercent !== null && (
        <>
          <progress
            value={item.progressPercent}
            max={100}
            aria-label="Download progress"
            className="h-1.5 w-20 shrink-0"
          />
          <span className="shrink-0 text-muted-foreground">
            {Math.round(item.progressPercent)}%
          </span>
        </>
      )}
    </div>
  )
}

// Only what the API reports; the file is shown by name, never by path.
function DownloadDetails({ item }: { item: DownloadQueueItem }) {
  return (
    <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-1 border-t bg-muted/30 px-4 py-3 text-sm">
      <dt className="text-muted-foreground">File</dt>
      <dd className="break-words">{downloadFileName(item.fileName)}</dd>
      <dt className="text-muted-foreground">Queued</dt>
      <dd>{formatTime(item.createdAt)}</dd>
      <dt className="text-muted-foreground">Updated</dt>
      <dd>{formatTime(item.updatedAt)}</dd>
      {item.finishedAt !== null && (
        <>
          <dt className="text-muted-foreground">Finished</dt>
          <dd>{formatTime(item.finishedAt)}</dd>
        </>
      )}
      {item.progressPercent !== null && isDownloadActive(item) && (
        <>
          <dt className="text-muted-foreground">Progress</dt>
          <dd>{Math.round(item.progressPercent)}%</dd>
        </>
      )}
      {item.error !== null && (
        <>
          <dt className="text-muted-foreground">Error</dt>
          <dd className="break-words whitespace-pre-wrap text-destructive">
            {item.error}
          </dd>
        </>
      )}
    </dl>
  )
}

function DownloadActions({
  item,
  label,
  state,
}: {
  item: DownloadQueueItem
  label: string
  state: DownloadQueueState
}) {
  const busy = state.busyIds.has(item.id)
  return (
    <div className="flex shrink-0 gap-1">
      {canRetryDownload(item) && (
        <Button
          variant="outline"
          size="sm"
          disabled={busy}
          aria-label={`Retry ${label}`}
          onClick={() => void state.retry(item.id)}
        >
          <RotateCw data-icon="inline-start" />
          Retry
        </Button>
      )}
      {canRemoveDownload(item) && (
        <Button
          variant="outline"
          size="sm"
          disabled={busy}
          aria-label={`Remove ${label} from the queue`}
          onClick={() => void state.remove(item.id)}
        >
          <X data-icon="inline-start" />
          Remove
        </Button>
      )}
    </div>
  )
}

// One compact row. The title toggles the details; the actions sit beside the toggle, not inside it, so
// expanding never interferes with retry or remove.
function DownloadItem({
  item,
  state,
}: {
  item: DownloadQueueItem
  state: DownloadQueueState
}) {
  const title = item.trackTitle ?? downloadFileName(item.fileName)
  const artist = item.artists.join(", ")
  const label = artist ? `${title} by ${artist}` : title
  return (
    <li>
      <Collapsible className="group/row">
        <div className="flex min-h-12 flex-wrap items-center gap-x-3 gap-y-1 px-3 py-1.5 md:flex-nowrap">
          <CollapsibleTrigger
            aria-label={`Details of ${label}`}
            className="flex min-w-0 flex-1 basis-48 items-center gap-2 rounded-md py-1 text-left outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            <ChevronRight
              aria-hidden
              className="size-4 shrink-0 transition-transform group-data-[open]/row:rotate-90"
            />
            <span className="min-w-0">
              <span className="block truncate font-medium">{title}</span>
              {artist && (
                <span className="block truncate text-sm text-muted-foreground">
                  {artist}
                </span>
              )}
            </span>
          </CollapsibleTrigger>
          <StatusCell item={item} />
          <span className="hidden shrink-0 text-xs text-muted-foreground xl:block xl:w-40">
            {formatTime(item.createdAt)}
          </span>
          <DownloadActions item={item} label={label} state={state} />
        </div>
        <CollapsibleContent>
          <DownloadDetails item={item} />
        </CollapsibleContent>
      </Collapsible>
    </li>
  )
}

const filterOptions: { value: DownloadStatusFilter; label: string }[] = [
  { value: "all", label: "All" },
  ...downloadStatuses.map((value) => ({ value, label: statusLabels[value] })),
]

function StatusFilter({
  status,
  onChange,
}: {
  status: DownloadStatusFilter
  onChange: (status: DownloadStatusFilter) => void
}) {
  return (
    <div role="group" aria-label="Filter by status" className="flex flex-wrap gap-2">
      {filterOptions.map((option) => (
        <Button
          key={option.value}
          variant={status === option.value ? "secondary" : "outline"}
          size="sm"
          aria-pressed={status === option.value}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </Button>
      ))}
    </div>
  )
}

function DownloadsBody({
  state,
  status,
}: {
  state: DownloadQueueState
  status: DownloadStatusFilter
}) {
  const { items, error } = state
  if (items === null)
    return error ? null : <Skeleton className="h-40 w-full rounded-xl" />

  if (items.length === 0)
    return (
      <p className="rounded-xl border bg-card p-6 text-center text-muted-foreground">
        {status === "all"
          ? "No downloads have been queued yet."
          : `No downloads with the status ${statusLabels[status]}.`}
      </p>
    )

  return (
    <ul aria-label="Downloads" className="divide-y rounded-xl border bg-card">
      {items.map((item) => (
        <DownloadItem key={item.id} item={item} state={state} />
      ))}
    </ul>
  )
}

// Overview of the download queue, current and past requests, narrowed by status. A failed request can be retried and a failed or
// completed one removed from the list (the wishlist and stored music are untouched); an active transfer has no
// controls, as it cannot be cancelled.
export function DownloadsView({
  state,
  status,
  onStatusChange,
  className,
}: {
  state: DownloadQueueState
  status: DownloadStatusFilter
  onStatusChange: (status: DownloadStatusFilter) => void
  className?: string
}) {
  return (
    <section
      aria-labelledby="downloads-heading"
      className={cn("flex min-h-0 min-w-0 flex-col gap-4 overflow-auto", className)}
    >
      <div className="flex shrink-0 flex-wrap items-center gap-3">
        <div>
          <h1 id="downloads-heading" className="text-xl font-semibold">
            Downloads
          </h1>
          <p className="text-muted-foreground">
            Queued, active, completed and failed download requests
          </p>
        </div>
        {state.items !== null && (
          <span role="status" className="text-lg text-muted-foreground">
            {state.total} {state.total === 1 ? "request" : "requests"}
          </span>
        )}
        <Button
          variant="outline"
          size="lg"
          className="ml-auto"
          onClick={() => void state.refresh()}
          disabled={state.refreshing}
        >
          <RefreshCw data-icon="inline-start" />
          Refresh
        </Button>
      </div>
      <StatusFilter status={status} onChange={onStatusChange} />
      {state.error && (
        <p role="alert" className="shrink-0 text-sm text-destructive">
          {state.error}
        </p>
      )}
      {state.actionError && (
        <p role="alert" className="shrink-0 text-sm text-destructive">
          {state.actionError}
        </p>
      )}
      <DownloadsBody state={state} status={status} />
      {state.items !== null && state.total > state.items.length && (
        <p className="shrink-0 text-sm text-muted-foreground">
          Showing the newest {state.items.length} of {state.total} requests.
        </p>
      )}
    </section>
  )
}
