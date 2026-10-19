import { Search, SlidersHorizontal, UserRound } from "lucide-react"
import { useId, useState, type ReactNode } from "react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { Skeleton } from "@/components/ui/skeleton"
import { artistNames } from "@/lib/catalog-api"
import { useGlobalSearch } from "@/lib/use-global-search"
import { useSearchFields } from "@/lib/use-search-fields"
import { cn } from "@/lib/utils"

interface GlobalSearchProps {
  onSelectTrack: (trackId: number) => void
  onSelectArtist: (artistId: number) => void
  className?: string
}

function Group({
  label,
  empty,
  children,
}: {
  label: string
  empty: boolean
  children: ReactNode
}) {
  return (
    <section aria-label={label} className="flex flex-col gap-0.5">
      <h3 className="px-2 pt-1 text-xs font-medium text-muted-foreground">
        {label}
      </h3>
      {empty ? (
        <p className="px-2 py-1.5 text-xs text-muted-foreground">
          No {label.toLowerCase()} match.
        </p>
      ) : (
        <ul>{children}</ul>
      )}
    </section>
  )
}

const rowClassName =
  "flex h-auto w-full justify-start gap-2 py-1.5 text-left font-normal"

// Searches the local catalog from anywhere in the app. Which fields it looks in is a
// per-browser preference.
export function GlobalSearch({
  onSelectTrack,
  onSelectArtist,
  className,
}: GlobalSearchProps) {
  const [input, setInput] = useState("")
  const [open, setOpen] = useState(false)
  const { fields, setField } = useSearchFields()
  const search = useGlobalSearch(input, fields)
  const resultsId = useId()
  const showResults = open && search.status !== "idle"

  const choose = (select: () => void) => {
    select()
    setOpen(false)
  }

  return (
    <div
      className={cn("relative flex items-center gap-1", className)}
      onFocus={() => setOpen(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget)) setOpen(false)
      }}
      onKeyDown={(event) => {
        if (event.key === "Escape") setOpen(false)
      }}
    >
      <div className="relative flex-1">
        <Search
          className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <Input
          type="search"
          value={input}
          onChange={(event) => {
            setInput(event.target.value)
            setOpen(true)
          }}
          placeholder="Search the catalog..."
          aria-label="Search the catalog"
          aria-controls={showResults ? resultsId : undefined}
          className="pl-7"
        />
      </div>
      <Popover>
        <PopoverTrigger
          render={
            <Button
              variant="ghost"
              size="icon-lg"
              aria-label="Search fields"
            />
          }
        >
          <SlidersHorizontal />
        </PopoverTrigger>
        <PopoverContent align="end" className="w-48 gap-2">
          <p className="text-xs font-medium">Search in</p>
          {(
            [
              ["title", "Title"],
              ["artist", "Artist"],
            ] as const
          ).map(([field, label]) => (
            <label key={field} className="flex items-center gap-2 text-xs">
              <Checkbox
                checked={fields[field]}
                onCheckedChange={(checked) => setField(field, checked)}
              />
              {label}
            </label>
          ))}
        </PopoverContent>
      </Popover>
      {showResults && (
        <div
          id={resultsId}
          className="absolute top-full right-0 left-0 z-50 mt-1 flex max-h-[70dvh] flex-col gap-2 overflow-y-auto rounded-lg border bg-popover p-1 text-popover-foreground shadow-md"
        >
          {search.status === "loading" && (
            <div
              role="status"
              aria-label="Searching"
              className="flex flex-col gap-1.5 p-2"
            >
              <Skeleton className="h-4 w-2/3" />
              <Skeleton className="h-4 w-1/2" />
              <Skeleton className="h-4 w-3/5" />
            </div>
          )}
          {search.status === "error" && (
            <div role="alert" className="flex items-center gap-2 p-2 text-xs">
              <span className="flex-1">The search failed.</span>
              <Button size="sm" variant="outline" onClick={search.retry}>
                Retry
              </Button>
            </div>
          )}
          {search.status === "ready" && (
            <>
              <Group label="Tracks" empty={search.tracks.length === 0}>
                {search.tracks.map((track) => (
                  <li key={track.id}>
                    <Button
                      variant="ghost"
                      className={rowClassName}
                      onClick={() => choose(() => onSelectTrack(track.id))}
                    >
                      <Badge variant="outline">Track</Badge>
                      <span className="min-w-0 flex-1 truncate">
                        {track.title}
                        {track.version && (
                          <span className="text-muted-foreground">
                            {" "}
                            ({track.version})
                          </span>
                        )}
                      </span>
                      <span className="max-w-[40%] truncate text-muted-foreground">
                        {artistNames(track)}
                      </span>
                    </Button>
                  </li>
                ))}
              </Group>
              {fields.artist && (
                <Group label="Artists" empty={search.artists.length === 0}>
                  {search.artists.map((artist) => (
                    <li key={artist.id}>
                      <Button
                        variant="ghost"
                        className={rowClassName}
                        onClick={() =>
                          choose(() => onSelectArtist(artist.id))
                        }
                      >
                        <Badge variant="secondary">
                          <UserRound data-icon="inline-start" />
                          Artist
                        </Badge>
                        <span className="min-w-0 flex-1 truncate">
                          {artist.name}
                        </span>
                        <span className="text-muted-foreground">
                          {artist.trackCount}{" "}
                          {artist.trackCount === 1 ? "track" : "tracks"}
                        </span>
                      </Button>
                    </li>
                  ))}
                </Group>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )
}
