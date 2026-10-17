import { ArrowDownUp, LayoutList, MoreHorizontal, Play } from "lucide-react"

import { CoverArt } from "@/components/cover-art"
import { TrackStatusBadge } from "@/components/track-status-badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import type { Track } from "@/data/mock-tracks"
import { formatDuration } from "@/lib/format"
import { cn } from "@/lib/utils"

interface TrackTableProps {
  tracks: Track[]
  selectedId: string | null
  nowPlayingId: string
  onSelect: (id: string) => void
  // The mobile Library page has its own header with the Filters button.
  showHeader?: boolean
}

export function TrackTable({
  tracks,
  selectedId,
  nowPlayingId,
  onSelect,
  showHeader = true,
}: TrackTableProps) {
  return (
    <section
      aria-labelledby={showHeader ? "library-heading" : undefined}
      aria-label={showHeader ? undefined : "Library tracks"}
      className="flex min-h-0 min-w-0 flex-col rounded-xl border bg-card"
    >
      {showHeader && (
        <div className="flex flex-wrap items-center gap-3 p-4">
          <h1 id="library-heading" className="text-xl font-semibold">
            Library
          </h1>
          <span className="text-lg text-muted-foreground">
            {tracks.length} tracks
          </span>
          <div className="ml-auto flex items-center gap-2">
            <Button
              variant="outline"
              size="icon-lg"
              disabled
              aria-label="Change view"
              title="Not available yet"
            >
              <LayoutList />
            </Button>
            <Button
              variant="outline"
              size="lg"
              disabled
              title="Not available yet"
            >
              <ArrowDownUp data-icon="inline-start" />
              Sort: Artist
            </Button>
          </div>
        </div>
      )}
      <div className="min-h-0 flex-1 overflow-auto">
        <Table className="min-w-[48rem] text-sm">
          <TableHeader className="sticky top-0 z-10 bg-card">
            <TableRow className="hover:bg-transparent">
              <TableHead className="w-10 pl-4">
                <Checkbox disabled aria-label="Select all tracks" />
              </TableHead>
              <TableHead className="w-12">
                <span className="sr-only">Cover</span>
              </TableHead>
              <TableHead>Title</TableHead>
              <TableHead>Artist</TableHead>
              <TableHead>Album</TableHead>
              <TableHead>Year</TableHead>
              <TableHead>Duration</TableHead>
              <TableHead>Status</TableHead>
              <TableHead className="w-12 pr-4">
                <span className="sr-only">Actions</span>
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {tracks.map((track) => {
              const selected = track.id === selectedId
              const playing = track.id === nowPlayingId
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
                  <TableCell className="pl-4">
                    {playing ? (
                      <Play
                        className="size-4 text-primary"
                        aria-label="Now playing"
                      />
                    ) : (
                      <Checkbox disabled aria-label={`Select ${track.title}`} />
                    )}
                  </TableCell>
                  <TableCell>
                    <CoverArt track={track} className="size-9 text-xs" />
                  </TableCell>
                  <TableCell className="max-w-56 truncate font-medium">
                    {track.title}
                  </TableCell>
                  <TableCell className="max-w-44 truncate">
                    {track.artist}
                  </TableCell>
                  <TableCell className="max-w-44 truncate">
                    {track.album}
                  </TableCell>
                  <TableCell>{track.year}</TableCell>
                  <TableCell>{formatDuration(track.durationSeconds)}</TableCell>
                  <TableCell>
                    <TrackStatusBadge status={track.status} />
                  </TableCell>
                  <TableCell className="pr-4">
                    <Button
                      variant="ghost"
                      size="icon-sm"
                      disabled
                      aria-label={`Actions for ${track.title}`}
                      title="Not available yet"
                    >
                      <MoreHorizontal />
                    </Button>
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </div>
    </section>
  )
}
