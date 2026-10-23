import { Search, SlidersHorizontal } from "lucide-react"
import { useEffect, useRef, useState, type ReactNode } from "react"

import { AlbumDetail } from "@/components/album-detail"
import { AddToLibraryButton } from "@/components/add-to-library-button"
import { FailureAlert, LoadingRows } from "@/components/result-states"
import { ProviderTrackRow, SourceLink } from "@/components/provider-track-row"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { albumSubtitle, type ExternalAlbum } from "@/lib/album-api"
import { providerLabel, type ArtistRef } from "@/lib/artist"
import { addExternalTrackToLibrary, type ExternalArtist, type ExternalTrack } from "@/lib/catalog-api"
import {
  canShowMore,
  categories,
  categoryLabels,
  limitFor,
  type Category,
  type ResultKind,
} from "@/lib/search-state"
import type { SearchStatus } from "@/lib/use-debounced-search"
import { useExternalSearch } from "@/lib/use-external-search"
import { useSearchFields } from "@/lib/use-search-fields"
import { cn } from "@/lib/utils"

interface SearchPageProps {
  query: string
  onQueryChange: (query: string) => void
  onSelectArtist: (artist: ArtistRef) => void
  // Put the cursor in the search box when the page opens (desktop; on a phone it
  // would raise the keyboard over the page).
  autoFocus?: boolean
  className?: string
}

interface Results<T> {
  status: SearchStatus
  error?: string
  items: T[]
  retry: () => void
}

// One kind of result: its loading or error state while it isn't ready, then its rows,
// and a way to see more of them. Everything stays inside the page.
function Group<T>({
  label,
  results,
  limit,
  pages,
  focused,
  onShowMore,
  emptyHint,
  renderRow,
}: {
  label: string
  results: Results<T>
  limit: number
  // The "Show more" pages already loaded behind the first one.
  pages: number
  // The category is open on its own; otherwise this is a few rows of the overview.
  focused: boolean
  onShowMore: () => void
  emptyHint?: string
  renderRow: (item: T) => ReactNode
}) {
  const { status, error, items, retry } = results
  let body: ReactNode = null
  if (status === "loading") {
    body = <LoadingRows label={`Searching ${label.toLowerCase()}`} />
  } else if (status === "error") {
    body = (
      <FailureAlert
        message={error ?? `The ${label.toLowerCase()} search failed.`}
        onRetry={retry}
      />
    )
  } else if (status === "ready" && items.length === 0) {
    body = (
      <p className="px-2 py-1.5 text-sm text-muted-foreground">
        {emptyHint ?? `No ${label.toLowerCase()} match.`}
      </p>
    )
  } else if (status === "ready") {
    body = (
      <>
        <ul>{items.map(renderRow)}</ul>
        {canShowMore(items.length, limit, pages) && (
          <Button
            variant="ghost"
            size="sm"
            className="self-start"
            onClick={onShowMore}
          >
            {focused ? "Show more" : `Show all ${label.toLowerCase()}`}
          </Button>
        )}
      </>
    )
  }
  return (
    <section aria-label={label} className="flex min-w-0 flex-col gap-1">
      <h2 className="px-2 text-sm font-medium">{label}</h2>
      {body}
    </section>
  )
}

function ArtistRow({
  artist,
  onSelect,
}: {
  artist: ExternalArtist
  onSelect: (artist: ArtistRef) => void
}) {
  return (
    <li className="flex flex-wrap items-center gap-x-2 gap-y-1 px-2 py-1.5 text-sm">
      <Badge variant="outline">{providerLabel(artist.provider)}</Badge>
      <Button
        variant="link"
        className="h-auto min-w-0 flex-1 justify-start p-0 text-left"
        onClick={() =>
          onSelect({
            kind: "external",
            provider: artist.provider,
            externalId: artist.externalId,
            name: artist.name,
          })
        }
      >
        <span className="truncate">{artist.name}</span>
      </Button>
      {artist.disambiguation && (
        <span className="max-w-[40%] truncate text-muted-foreground">
          {artist.disambiguation}
        </span>
      )}
      <SourceLink
        title={artist.name}
        source={{ label: providerLabel(artist.provider), url: artist.url }}
      />
    </li>
  )
}

function TrackRow({ track }: { track: ExternalTrack }) {
  return (
    <ProviderTrackRow
      title={
        track.disambiguation
          ? `${track.title} (${track.disambiguation})`
          : track.title
      }
      subtitle={track.artists.map((a) => a.name).join(", ")}
      availability="external"
      source={{ label: providerLabel(track.provider), url: track.url }}
      actions={
        <AddToLibraryButton onAdd={() => addExternalTrackToLibrary(track)} />
      }
    />
  )
}

// External search as a full page: the box stays on top and the results below it update
// in place, never in a popup. Albums (compilations included), Tracks and Artists show a
// few rows each, and a category opens on its own. Which fields it looks in is a
// per-browser preference. Viewing a result stores nothing: only adding does.
export function SearchPage({
  query,
  onQueryChange,
  onSelectArtist,
  autoFocus = false,
  className,
}: SearchPageProps) {
  const [category, setCategory] = useState<Category>("all")
  // How many "Show more" pages the focused category has been asked for.
  const [pages, setPages] = useState(0)
  const [album, setAlbum] = useState<ExternalAlbum | null>(null)
  const { fields, setField } = useSearchFields()
  const limits = {
    albums: limitFor("albums", category),
    tracks: limitFor("tracks", category),
    artists: limitFor("artists", category),
  }
  const external = useExternalSearch(query, fields, limits, pages)
  const inputRef = useRef<HTMLInputElement>(null)
  const focused = category !== "all"
  const idle = query.trim() === ""

  useEffect(() => {
    if (autoFocus) inputRef.current?.focus()
  }, [autoFocus])

  const changeQuery = (value: string) => {
    setAlbum(null)
    setPages(0)
    onQueryChange(value)
  }
  const changeCategory = (value: Category) => {
    setPages(0)
    setCategory(value)
  }
  const showMore = (kind: ResultKind) =>
    focused ? setPages((n) => n + 1) : changeCategory(kind)
  const shows = (kind: ResultKind) => category === "all" || category === kind

  return (
    <div className={cn("flex min-h-0 flex-col gap-3", className)}>
      <h1 className="sr-only">Search</h1>
      <div className="flex items-center gap-1">
        <div className="relative flex-1">
          <Search
            className="pointer-events-none absolute top-1/2 left-2 size-3.5 -translate-y-1/2 text-muted-foreground"
            aria-hidden="true"
          />
          <Input
            ref={inputRef}
            type="search"
            value={query}
            onChange={(event) => changeQuery(event.target.value)}
            placeholder="Search albums, tracks and artists..."
            aria-label="Search albums, tracks and artists"
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
            <p className="text-xs font-medium">Search tracks in</p>
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
      </div>
      {album ? (
        <AlbumDetail album={album} onBack={() => setAlbum(null)} />
      ) : (
        <>
          <Tabs
            value={category}
            onValueChange={(value) => changeCategory(value as Category)}
          >
            <TabsList className="w-full">
              {categories.map((value) => (
                <TabsTrigger key={value} value={value}>
                  {categoryLabels[value]}
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
          {idle ? (
            <p className="p-2 text-sm text-muted-foreground">
              Search for albums, tracks and artists.
            </p>
          ) : (
            <div
              aria-live="polite"
              className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto"
            >
              {shows("albums") && (
                <Group
                  label="Albums"
                  results={external.albums}
                  limit={limits.albums}
                  pages={pages}
                  focused={focused}
                  onShowMore={() => showMore("albums")}
                  renderRow={(item) => (
                    <ProviderTrackRow
                      key={`${item.provider}:${item.externalId}`}
                      title={item.title}
                      subtitle={albumSubtitle(item)}
                      onSelect={() => setAlbum(item)}
                      source={{
                        label: providerLabel(item.provider),
                        url: item.url,
                      }}
                    />
                  )}
                />
              )}
              {shows("tracks") && (
                <Group
                  label="Tracks"
                  results={external.tracks}
                  limit={limits.tracks}
                  pages={pages}
                  focused={focused}
                  onShowMore={() => showMore("tracks")}
                  renderRow={(item) => (
                    <TrackRow
                      key={`${item.provider}:${item.externalId}`}
                      track={item}
                    />
                  )}
                />
              )}
              {shows("artists") && (fields.artist || focused) && (
                <Group
                  label="Artists"
                  results={external.artists}
                  limit={limits.artists}
                  pages={pages}
                  focused={focused}
                  onShowMore={() => showMore("artists")}
                  emptyHint={
                    fields.artist
                      ? undefined
                      : "Artist results are off. Turn on Artist in Search fields."
                  }
                  renderRow={(item) => (
                    <ArtistRow
                      key={`${item.provider}:${item.externalId}`}
                      artist={item}
                      onSelect={onSelectArtist}
                    />
                  )}
                />
              )}
            </div>
          )}
        </>
      )}
    </div>
  )
}
