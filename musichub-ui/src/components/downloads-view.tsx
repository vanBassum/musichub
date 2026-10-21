import { RefreshCw, RotateCw, X } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import {
  canRemoveDownload,
  canRetryDownload,
  downloadStatuses,
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

const statusClassNames: Record<DownloadQueueStatus, string> = {
  Queued: "bg-muted text-muted-foreground",
  Downloading: "bg-primary text-primary-foreground",
  Downloaded: "bg-primary text-primary-foreground",
  Completed: "bg-success text-success-foreground",
  Failed: "bg-destructive text-white",
}

const formatTime = (iso: string) => new Date(iso).toLocaleString()

function Timestamps({ item }: { item: DownloadQueueItem }) {
  return (
    <p className="mt-1 text-xs text-muted-foreground">
      Queued {formatTime(item.createdAt)}
      {item.finishedAt !== null
        ? ` · Finished ${formatTime(item.finishedAt)}`
        : ` · Updated ${formatTime(item.updatedAt)}`}
    </p>
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
    <div className="flex gap-2">
      {canRetryDownload(item) && (
        <Button
          variant="outline"
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

function DownloadItem({
  item,
  state,
}: {
  item: DownloadQueueItem
  state: DownloadQueueState
}) {
  const title = item.trackTitle ?? item.fileName
  const artist = item.artists.join(", ")
  const label = artist ? `${title} by ${artist}` : title
  return (
    <li className="flex flex-wrap items-start gap-3 p-4">
      <div className="min-w-0 flex-1 basis-64">
        <p className="font-medium break-words">{title}</p>
        {artist && (
          <p className="text-sm break-words text-muted-foreground">{artist}</p>
        )}
        {item.progressPercent !== null && item.status === "Downloading" && (
          <progress
            value={item.progressPercent}
            max={100}
            aria-label={`Download progress of ${label}`}
            className="mt-2 h-1.5 w-full max-w-xs"
          />
        )}
        {item.error !== null && (
          <p role="alert" className="mt-1 text-sm break-words text-destructive">
            {item.error}
          </p>
        )}
        <Timestamps item={item} />
      </div>
      <div className="flex items-center gap-2">
        {item.status === "Downloading" && item.progressPercent !== null && (
          <span className="text-sm text-muted-foreground">
            {Math.round(item.progressPercent)}%
          </span>
        )}
        <Badge className={statusClassNames[item.status]}>
          {statusLabels[item.status]}
        </Badge>
        <DownloadActions item={item} label={label} state={state} />
      </div>
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
