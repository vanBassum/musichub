import {
  ExternalLink,
  Search,
  SlidersHorizontal,
  UserRound,
} from "lucide-react"
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
import { WishlistButton } from "@/components/wishlist-button"
import { artistNames, wantExternalTrack } from "@/lib/catalog-api"
import { useExternalSearch } from "@/lib/use-external-search"
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

const providerLabels: Record<string, string> = { musicbrainz: "MusicBrainz" }

function ExternalRow({
  provider,
  url,
  kind,
  name,
  detail,
  action,
}: {
  provider: string
  url: string
  kind: "Track" | "Artist"
  name: string
  detail?: string
  action?: ReactNode
}) {
  const label = providerLabels[provider] ?? provider
  return (
    <li className="flex items-center gap-2 px-2 py-1.5 text-sm">
      <Badge variant="outline">{kind}</Badge>
      <span className="min-w-0 flex-1 truncate">{name}</span>
      {detail && (
        <span className="max-w-[40%] truncate text-muted-foreground">
          {detail}
        </span>
      )}
      <a
        href={url}
        target="_blank"
        rel="noreferrer"
        aria-label={`Open on ${label}`}
        className="flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
      >
        {label}
        <ExternalLink className="size-3" aria-hidden="true" />
      </a>
      {action}
    </li>
  )
}

// Results of the external provider. They are not catalog items: they can't be opened
// here, and viewing them stores nothing. Only the explicit "Add to wishlist" does.
function ExternalResults({
  search,
  showArtists,
}: {
  search: ReturnType<typeof useExternalSearch>
  showArtists: boolean
}) {
  return (
    <section
      aria-label="External results"
      className="flex flex-col gap-0.5 rounded-md border border-dashed p-1"
    >
      <h3 className="px-2 pt-1 text-xs font-medium text-muted-foreground">
        External results (not in your catalog)
      </h3>
      {search.status === "loading" && (
        <div
          role="status"
          aria-label="Searching external sources"
          className="flex flex-col gap-1.5 p-2"
        >
          <Skeleton className="h-4 w-3/5" />
          <Skeleton className="h-4 w-1/2" />
        </div>
      )}
      {search.status === "error" && (
        <div role="alert" className="flex items-center gap-2 p-2 text-xs">
          <span className="flex-1">
            {search.error ?? "The external search failed."}
          </span>
          <Button size="sm" variant="outline" onClick={search.retry}>
            Retry
          </Button>
        </div>
      )}
      {search.status === "ready" &&
        (search.tracks.length === 0 &&
        (!showArtists || search.artists.length === 0) ? (
          <p className="px-2 py-1.5 text-xs text-muted-foreground">
            No external matches.
          </p>
        ) : (
          <ul>
            {search.tracks.map((track) => (
              <ExternalRow
                key={`${track.provider}:track:${track.externalId}`}
                provider={track.provider}
                url={track.url}
                kind="Track"
                name={
                  track.disambiguation
                    ? `${track.title} (${track.disambiguation})`
                    : track.title
                }
                detail={track.artists.map((a) => a.name).join(", ")}
                action={
                  <WishlistButton onWant={() => wantExternalTrack(track)} />
                }
              />
            ))}
            {showArtists &&
              search.artists.map((artist) => (
                <ExternalRow
                  key={`${artist.provider}:artist:${artist.externalId}`}
                  provider={artist.provider}
                  url={artist.url}
                  kind="Artist"
                  name={artist.name}
                  detail={artist.disambiguation ?? undefined}
                />
              ))}
          </ul>
        ))}
    </section>
  )
}

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
  const external = useExternalSearch(input, fields)
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
          // Safari (macOS, iOS) doesn't focus buttons on click, so a press in the
          // list would blur the input and close the list before the click lands.
          // Keeping focus where it is makes selection work with mouse and touch.
          onMouseDown={(event) => event.preventDefault()}
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
          <ExternalResults search={external} showArtists={fields.artist} />
        </div>
      )}
    </div>
  )
}
