import { RefreshCw } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import {
  downloadQueueIsSample,
  type DownloadQueueItem,
  type DownloadQueueStatus,
} from "@/lib/downloads-api"
import type { DownloadQueueState } from "@/lib/use-download-queue"
import { cn } from "@/lib/utils"

const statusLabels: Record<DownloadQueueStatus, string> = {
  queued: "Queued",
  downloading: "Downloading",
  completed: "Completed",
  failed: "Failed",
}

const statusClassNames: Record<DownloadQueueStatus, string> = {
  queued: "bg-muted text-muted-foreground",
  downloading: "bg-primary text-primary-foreground",
  completed: "bg-success text-success-foreground",
  failed: "bg-destructive text-white",
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

function DownloadItem({ item }: { item: DownloadQueueItem }) {
  const label = `${item.trackTitle} by ${item.artist}`
  return (
    <li className="flex flex-wrap items-start gap-3 p-4">
      <div className="min-w-0 flex-1 basis-64">
        <p className="font-medium break-words">{item.trackTitle}</p>
        <p className="text-sm break-words text-muted-foreground">{item.artist}</p>
        {item.progressPercent !== null && item.status === "downloading" && (
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
        {item.status === "downloading" && item.progressPercent !== null && (
          <span className="text-sm text-muted-foreground">
            {Math.round(item.progressPercent)}%
          </span>
        )}
        <Badge className={statusClassNames[item.status]}>
          {statusLabels[item.status]}
        </Badge>
      </div>
    </li>
  )
}

function DownloadsBody({ state }: { state: DownloadQueueState }) {
  const { items, error } = state
  if (items === null)
    return error ? null : <Skeleton className="h-40 w-full rounded-xl" />

  if (items.length === 0)
    return (
      <p className="rounded-xl border bg-card p-6 text-center text-muted-foreground">
        No downloads have been queued yet.
      </p>
    )

  return (
    <ul aria-label="Downloads" className="divide-y rounded-xl border bg-card">
      {items.map((item) => (
        <DownloadItem key={item.id} item={item} />
      ))}
    </ul>
  )
}

// Read-only overview of the download queue, current and past requests. It changes nothing: there are no pause,
// cancel or retry controls.
export function DownloadsView({
  state,
  className,
}: {
  state: DownloadQueueState
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
      {downloadQueueIsSample && (
        <p
          role="status"
          className="shrink-0 rounded-xl bg-warning px-4 py-3 text-sm text-warning-foreground"
        >
          The download queue is not connected yet: these are sample requests.
        </p>
      )}
      {state.error && (
        <p role="alert" className="shrink-0 text-sm text-destructive">
          {state.error}
        </p>
      )}
      <DownloadsBody state={state} />
    </section>
  )
}
