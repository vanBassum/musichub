import {
  CheckCircle2,
  ChevronRight,
  CircleDashed,
  Clock,
  Download,
  Loader2,
  RefreshCw,
  RotateCw,
  Search,
  SearchX,
  X,
  XCircle,
  type LucideIcon,
} from "lucide-react"

import { TrackActivityTimeline } from "@/components/track-activity-timeline"
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
} from "@/lib/downloads-api"
import type { WishlistModel } from "@/lib/use-wishlist"
import {
  wishlistStates,
  type WishlistFilter,
  type WishlistItem,
  type WishlistState,
} from "@/lib/wishlist-api"
import { cn } from "@/lib/utils"

const stateLabels: Record<WishlistState, string> = {
  Missing: "Missing",
  Searching: "Searching",
  NotFound: "Not found",
  Failed: "Failed",
  Queued: "Queued",
  Downloading: "Downloading",
  Downloaded: "Downloaded, waiting to import",
  Available: "Available",
}

const stateIcons: Record<WishlistState, LucideIcon> = {
  Missing: CircleDashed,
  Searching: Loader2,
  NotFound: SearchX,
  Failed: XCircle,
  Queued: Clock,
  Downloading: Download,
  Downloaded: Download,
  Available: CheckCircle2,
}

const stateTextClassNames: Record<WishlistState, string> = {
  Missing: "text-muted-foreground",
  Searching: "text-primary",
  NotFound: "text-warning",
  Failed: "text-destructive",
  Queued: "text-muted-foreground",
  Downloading: "text-primary",
  Downloaded: "text-primary",
  Available: "text-success",
}

const formatTime = (iso: string) => new Date(iso).toLocaleString()

// State as icon and text, plus what is relevant for it: a short error hint for a failed download.
function StateCell({ item }: { item: WishlistItem }) {
  const Icon = stateIcons[item.state]
  return (
    <div className="flex min-w-0 items-center gap-2 text-sm md:w-72">
      <span
        className={cn(
          "flex shrink-0 items-center gap-1.5 font-medium",
          stateTextClassNames[item.state]
        )}
      >
        <Icon
          aria-hidden
          className={cn("size-4", item.state === "Searching" && "animate-spin")}
        />
        {stateLabels[item.state]}
      </span>
      {item.state === "Failed" && item.error !== null && (
        <span className="truncate text-muted-foreground" title={item.error}>
          {downloadErrorHint(item.error)}
        </span>
      )}
    </div>
  )
}

// Search now for an idle track; Retry and Remove for the track's download request, where it has one. The
// API checks each again, so a stale row can only be refused, never do harm.
function WishlistActions({
  item,
  label,
  model,
}: {
  item: WishlistItem
  label: string
  model: WishlistModel
}) {
  const busy = model.busyIds.has(item.trackId)
  return (
    <div className="flex shrink-0 gap-1">
      {item.canSearch && (
        <Button
          variant="outline"
          size="sm"
          disabled={busy}
          aria-label={`Search now for ${label}`}
          onClick={() => void model.search(item)}
        >
          <Search data-icon="inline-start" />
          Search now
        </Button>
      )}
      {canRetryDownload(item.downloadStatus) && (
        <Button
          variant="outline"
          size="sm"
          disabled={busy}
          aria-label={`Retry ${label}`}
          onClick={() => void model.retry(item)}
        >
          <RotateCw data-icon="inline-start" />
          Retry
        </Button>
      )}
      {canRemoveDownload(item.downloadStatus) && (
        <Button
          variant="outline"
          size="sm"
          disabled={busy}
          aria-label={`Remove ${label} from the queue`}
          onClick={() => void model.remove(item)}
        >
          <X data-icon="inline-start" />
          Remove
        </Button>
      )}
    </div>
  )
}

// One compact row. The title toggles the activity timeline; the actions sit beside the toggle, not inside it,
// so expanding never interferes with them.
function WishlistRow({
  item,
  model,
}: {
  item: WishlistItem
  model: WishlistModel
}) {
  const title = item.version ? `${item.title} (${item.version})` : item.title
  const artist = item.artists.join(", ")
  const label = artist ? `${title} by ${artist}` : title
  return (
    <li>
      <Collapsible className="group/row">
        <div className="flex min-h-12 flex-wrap items-center gap-x-3 gap-y-1 px-3 py-1.5 md:flex-nowrap">
          <CollapsibleTrigger
            aria-label={`Activity of ${label}`}
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
          <StateCell item={item} />
          <span
            className="hidden shrink-0 text-xs text-muted-foreground xl:block xl:w-40"
            title={item.lastActivity ?? undefined}
          >
            {item.lastActivityAt ? formatTime(item.lastActivityAt) : "No activity yet"}
          </span>
          <WishlistActions item={item} label={label} model={model} />
        </div>
        <CollapsibleContent className="border-t bg-muted/30 px-4 py-3">
          <TrackActivityTimeline
            trackId={item.trackId}
            version={`${item.lastActivityAt ?? ""}|${item.state}`}
          />
        </CollapsibleContent>
      </Collapsible>
    </li>
  )
}

// "All" and the states that have tracks (the chosen one always), each with its count.
function StatusFilter({
  filter,
  counts,
  onChange,
}: {
  filter: WishlistFilter
  counts: NonNullable<WishlistModel["counts"]>
  onChange: (filter: WishlistFilter) => void
}) {
  const options: { value: WishlistFilter; label: string; count: number }[] = [
    {
      value: "all",
      label: "All",
      count: wishlistStates.reduce((sum, state) => sum + counts[state], 0),
    },
    ...wishlistStates
      .filter((state) => counts[state] > 0 || state === filter)
      .map((state) => ({
        value: state,
        label: stateLabels[state],
        count: counts[state],
      })),
  ]
  return (
    <div role="group" aria-label="Filter by state" className="flex flex-wrap gap-2">
      {options.map((option) => (
        <Button
          key={option.value}
          variant={filter === option.value ? "secondary" : "outline"}
          size="sm"
          aria-pressed={filter === option.value}
          onClick={() => onChange(option.value)}
        >
          {option.label} ({option.count})
        </Button>
      ))}
    </div>
  )
}

function WishlistBody({
  model,
  filter,
}: {
  model: WishlistModel
  filter: WishlistFilter
}) {
  const { items, error } = model
  if (items === null)
    return error ? null : <Skeleton className="h-40 w-full rounded-xl" />

  if (items.length === 0)
    return (
      <p className="rounded-xl border bg-card p-6 text-center text-muted-foreground">
        {filter === "all"
          ? "Nothing is wanted: every track in the library has a file."
          : `No tracks are ${stateLabels[filter].toLowerCase()}.`}
      </p>
    )

  return (
    <ul aria-label="Wishlist" className="divide-y rounded-xl border bg-card">
      {items.map((item) => (
        <WishlistRow key={item.trackId} item={item} model={model} />
      ))}
    </ul>
  )
}

// Overview of the wanted music: the catalog's tracks without a file and how far getting each one has come,
// narrowed by state. Each row opens its recorded activity. Search now schedules a search; a failed download
// can be retried and a failed or completed one removed from the queue (the catalog and stored music are
// untouched); an active transfer has no controls, as it cannot be cancelled.
export function WishlistView({
  model,
  filter,
  onFilterChange,
  className,
}: {
  model: WishlistModel
  filter: WishlistFilter
  onFilterChange: (filter: WishlistFilter) => void
  className?: string
}) {
  return (
    <section
      aria-labelledby="wishlist-heading"
      className={cn("flex min-h-0 min-w-0 flex-col gap-4 overflow-auto", className)}
    >
      <div className="flex shrink-0 flex-wrap items-center gap-3">
        <div>
          <h1 id="wishlist-heading" className="text-xl font-semibold">
            Wishlist
          </h1>
          <p className="text-muted-foreground">
            Wanted tracks and how far getting each one has come
          </p>
        </div>
        {model.items !== null && (
          <span role="status" className="text-lg text-muted-foreground">
            {model.total} {model.total === 1 ? "track" : "tracks"}
          </span>
        )}
        <Button
          variant="outline"
          size="lg"
          className="ml-auto"
          onClick={() => void model.refresh()}
          disabled={model.refreshing}
        >
          <RefreshCw data-icon="inline-start" />
          Refresh
        </Button>
      </div>
      {model.counts !== null && (
        <StatusFilter filter={filter} counts={model.counts} onChange={onFilterChange} />
      )}
      {model.error && (
        <p role="alert" className="shrink-0 text-sm text-destructive">
          {model.error}
        </p>
      )}
      {model.actionError && (
        <p role="alert" className="shrink-0 text-sm text-destructive">
          {model.actionError}
        </p>
      )}
      {model.notice && (
        <p role="status" className="shrink-0 text-sm text-muted-foreground">
          {model.notice}
        </p>
      )}
      <WishlistBody model={model} filter={filter} />
      {model.items !== null && model.total > model.items.length && (
        <p className="shrink-0 text-sm text-muted-foreground">
          Showing the {model.items.length} most recent of {model.total} tracks.
        </p>
      )}
    </section>
  )
}
