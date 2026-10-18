import { AudioBadge } from "@/components/audio-badge"
import { Button } from "@/components/ui/button"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { artistNames, type PagedTracks } from "@/lib/catalog-api"
import { formatDate } from "@/lib/format"
import type { CatalogStatus } from "@/lib/use-catalog"
import { cn } from "@/lib/utils"

interface TrackTableProps {
  status: CatalogStatus
  data: PagedTracks | undefined
  error: string | undefined
  onRetry: () => void
  onPageChange: (page: number) => void
  // Whether a search or filter is applied, to word the empty state.
  filtered: boolean
  selectedId: number | null
  onSelect: (id: number) => void
  // The mobile Library page has its own header with the Filters button.
  showHeader?: boolean
}

export function TrackTable({
  status,
  data,
  error,
  onRetry,
  onPageChange,
  filtered,
  selectedId,
  onSelect,
  showHeader = true,
}: TrackTableProps) {
  const tracks = data?.items ?? []
  const lastPage = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1

  return (
    <section
      aria-labelledby={showHeader ? "library-heading" : undefined}
      aria-label={showHeader ? undefined : "Library tracks"}
      aria-busy={status === "loading"}
      className="flex min-h-0 min-w-0 flex-col rounded-xl border bg-card"
    >
      {showHeader && (
        <div className="flex flex-wrap items-center gap-3 p-4">
          <h1 id="library-heading" className="text-xl font-semibold">
            Library
          </h1>
          {data && (
            <span className="text-lg text-muted-foreground">
              {data.total} tracks
            </span>
          )}
        </div>
      )}
      {status === "error" && (
        <div
          role="alert"
          className="mx-4 mb-2 flex items-center gap-3 rounded-md border border-destructive/40 p-3 text-sm"
        >
          <span className="text-destructive">
            Could not load tracks. {error}
          </span>
          <Button variant="outline" size="sm" onClick={onRetry}>
            Try again
          </Button>
        </div>
      )}
      <div
        className={cn(
          "min-h-0 flex-1 overflow-auto",
          status === "loading" && data && "opacity-60"
        )}
      >
        <Table className="min-w-[32rem] text-sm">
          <TableHeader className="sticky top-0 z-10 bg-card">
            <TableRow className="hover:bg-transparent">
              <TableHead className="pl-4">Title</TableHead>
              <TableHead>Artist</TableHead>
              <TableHead>Audio</TableHead>
              <TableHead className="pr-4">Added</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {tracks.map((track) => {
              const selected = track.id === selectedId
              return (
                <TableRow
                  key={track.id}
                  tabIndex={0}
                  aria-selected={selected}
                  onClick={() => onSelect(track.id)}
                  onKeyDown={(event) => {
                    if (
                      event.target === event.currentTarget &&
                      (event.key === "Enter" || event.key === " ")
                    ) {
                      event.preventDefault()
                      onSelect(track.id)
                    }
                  }}
                  className={cn(
                    "cursor-pointer outline-none focus-visible:ring-2 focus-visible:ring-ring/40 focus-visible:ring-inset",
                    selected && "bg-primary/20 hover:bg-primary/25"
                  )}
                >
                  <TableCell className="max-w-64 truncate pl-4 font-medium">
                    {track.title}
                    {track.version && (
                      <span className="font-normal text-muted-foreground">
                        {" "}
                        ({track.version})
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="max-w-56 truncate">
                    {artistNames(track) || (
                      <span className="text-muted-foreground">No artist</span>
                    )}
                  </TableCell>
                  <TableCell>
                    <AudioBadge hasAudio={track.hasAudio} />
                  </TableCell>
                  <TableCell className="pr-4">
                    {formatDate(track.createdAt)}
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
        {status === "loading" && !data && (
          <p role="status" className="p-4 text-sm text-muted-foreground">
            Loading tracks…
          </p>
        )}
        {status === "ready" && tracks.length === 0 && (
          <p className="p-4 text-sm text-muted-foreground">
            {filtered
              ? "No tracks match the search or filter."
              : "The catalog has no tracks yet."}
          </p>
        )}
      </div>
      {data && data.total > 0 && (
        <div className="flex items-center gap-2 border-t p-3 text-sm">
          <span className="text-muted-foreground">
            Page {data.page} of {lastPage}
          </span>
          <div className="ml-auto flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={data.page <= 1}
              onClick={() => onPageChange(data.page - 1)}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={data.page >= lastPage}
              onClick={() => onPageChange(data.page + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </section>
  )
}
