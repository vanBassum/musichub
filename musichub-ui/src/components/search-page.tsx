import {
  ExternalLink,
  Search,
  SlidersHorizontal,
  UserRound,
} from "lucide-react"
import { useEffect, useRef, useState, type ReactNode } from "react"

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
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { AddToLibraryButton } from "@/components/add-to-library-button"
import { AvailabilityBadge } from "@/components/availability-badge"
import { providerLabel, type ArtistRef } from "@/lib/artist"
import {
  addExternalTrackToLibrary,
  artistNames,
  catalogAvailability,
} from "@/lib/catalog-api"
import type { SearchStatus } from "@/lib/use-debounced-search"
import { useExternalSearch } from "@/lib/use-external-search"
import { useGlobalSearch } from "@/lib/use-global-search"
import { useSearchFields } from "@/lib/use-search-fields"
import { cn } from "@/lib/utils"

interface SearchPageProps {
  query: string
  onQueryChange: (query: string) => void
  onSelectTrack: (trackId: number) => void
  onSelectArtist: (artist: ArtistRef) => void
  // Put the cursor in the search box when the page opens (desktop; on a phone it
  // would raise the keyboard over the page).
  autoFocus?: boolean
  className?: string
}

type Scope = "all" | "local" | "external"

// One source (My Music or the external provider) in a group: its loading or
// error state while it isn't ready, then its rows.
function SourceRows({
  source,
  status,
  error,
  onRetry,
  children,
}: {
  source: string
  status: SearchStatus
  error?: string | null
  onRetry: () => void
  children: ReactNode
}) {
  if (status === "loading") {
    return (
      <div
        role="status"
        aria-label={`Searching ${source}`}
        className="flex flex-col gap-1.5 p-2"
      >
        <Skeleton className="h-4 w-3/5" />
        <Skeleton className="h-4 w-1/2" />
      </div>
    )
  }
  if (status === "error") {
    return (
      <div role="alert" className="flex items-center gap-2 p-2 text-xs">
        <span className="flex-1">
          {error ?? `The ${source} search failed.`}
        </span>
        <Button size="sm" variant="outline" onClick={onRetry}>
          Retry
        </Button>
      </div>
    )
  }
  if (status === "ready") return <ul>{children}</ul>
  return null
}

function Group({
  label,
  empty,
  children,
}: {
  label: string
  // Every source that is searched answered, and none had a match.
  empty: boolean
  children: ReactNode
}) {
  return (
    <section aria-label={label} className="flex min-w-0 flex-col gap-1">
      <h2 className="px-2 text-sm font-medium">{label}</h2>
      {children}
      {empty && (
        <p className="px-2 py-1.5 text-sm text-muted-foreground">
          No {label.toLowerCase()} match.
        </p>
      )}
    </section>
  )
}

const rowClassName = "flex flex-wrap items-center gap-x-2 gap-y-1 px-2 py-1.5 text-sm"

function ProviderLink({ provider, url }: { provider: string; url: string }) {
  const label = providerLabel(provider)
  return (
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
  )
}

type LocalSearch = ReturnType<typeof useGlobalSearch>
type ExternalSearch = ReturnType<typeof useExternalSearch>

// A result name that opens its page.
function RowLink({
  onClick,
  children,
}: {
  onClick: () => void
  children: ReactNode
}) {
  return (
    <Button
      variant="link"
      className="h-auto min-w-0 flex-1 justify-start p-0 text-left"
      onClick={onClick}
    >
      <span className="truncate">{children}</span>
    </Button>
  )
}

function LocalArtistRow({
  artist,
  onSelect,
}: {
  artist: LocalSearch["artists"][number]
  onSelect: (artist: ArtistRef) => void
}) {
  return (
    <li className={rowClassName}>
      <Badge variant="secondary">
        <UserRound data-icon="inline-start" />
        My Music
      </Badge>
      <RowLink onClick={() => onSelect({ kind: "local", id: artist.id })}>
        {artist.name}
      </RowLink>
      <span className="text-muted-foreground">
        {artist.trackCount} {artist.trackCount === 1 ? "track" : "tracks"}
      </span>
    </li>
  )
}

function ExternalArtistRow({
  artist,
  onSelect,
}: {
  artist: ExternalSearch["artists"][number]
  onSelect: (artist: ArtistRef) => void
}) {
  return (
    <li className={rowClassName}>
      <Badge variant="outline">{providerLabel(artist.provider)}</Badge>
      <RowLink
        onClick={() =>
          onSelect({
            kind: "external",
            provider: artist.provider,
            externalId: artist.externalId,
            name: artist.name,
          })
        }
      >
        {artist.name}
      </RowLink>
      {artist.disambiguation && (
        <span className="max-w-[40%] truncate text-muted-foreground">
          {artist.disambiguation}
        </span>
      )}
      <ProviderLink provider={artist.provider} url={artist.url} />
    </li>
  )
}

function LocalTrackRow({
  track,
  onSelect,
}: {
  track: LocalSearch["tracks"][number]
  onSelect: (trackId: number) => void
}) {
  return (
    <li className={rowClassName}>
      <Badge variant="secondary">My Music</Badge>
      <RowLink onClick={() => onSelect(track.id)}>
        {track.title}
        {track.version && ` (${track.version})`}
      </RowLink>
      <span className="max-w-[40%] truncate text-muted-foreground">
        {artistNames(track)}
      </span>
      <AvailabilityBadge availability={catalogAvailability(track.hasAudio)} />
    </li>
  )
}

function ExternalTrackRow({
  track,
}: {
  track: ExternalSearch["tracks"][number]
}) {
  return (
    <li className={rowClassName}>
      <Badge variant="outline">{providerLabel(track.provider)}</Badge>
      <span className="min-w-0 flex-1 truncate">
        {track.disambiguation
          ? `${track.title} (${track.disambiguation})`
          : track.title}
      </span>
      <span className="max-w-[40%] truncate text-muted-foreground">
        {track.artists.map((a) => a.name).join(", ")}
      </span>
      <ProviderLink provider={track.provider} url={track.url} />
      <AddToLibraryButton onAdd={() => addExternalTrackToLibrary(track)} />
    </li>
  )
}

// Searches the local catalog and the external provider; one page for desktop and
// phone. Which fields it looks in is a per-browser preference. Viewing a result
// stores nothing: only "Add to library" does.
export function SearchPage({
  query,
  onQueryChange,
  onSelectTrack,
  onSelectArtist,
  autoFocus = false,
  className,
}: SearchPageProps) {
  const [scope, setScope] = useState<Scope>("all")
  const { fields, setField } = useSearchFields()
  const local = useGlobalSearch(query, fields)
  const external = useExternalSearch(query, fields)
  const inputRef = useRef<HTMLInputElement>(null)
  const showLocal = scope !== "external"
  const showExternal = scope !== "local"

  useEffect(() => {
    if (autoFocus) inputRef.current?.focus()
  }, [autoFocus])

  const sources = [
    showLocal && local.status,
    showExternal && external.status,
  ].filter((status): status is SearchStatus => status !== false)
  const allReady = sources.every((status) => status === "ready")
  const artistsEmpty =
    allReady &&
    (!showLocal || local.artists.length === 0) &&
    (!showExternal || external.artists.length === 0)
  const tracksEmpty =
    allReady &&
    (!showLocal || local.tracks.length === 0) &&
    (!showExternal || external.tracks.length === 0)

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
            onChange={(event) => onQueryChange(event.target.value)}
            placeholder="Search the catalog..."
            aria-label="Search the catalog"
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
      </div>
      <Tabs value={scope} onValueChange={(value) => setScope(value as Scope)}>
        <TabsList className="w-full">
          <TabsTrigger value="all">All</TabsTrigger>
          <TabsTrigger value="local">My Music</TabsTrigger>
          <TabsTrigger value="external">External</TabsTrigger>
        </TabsList>
      </Tabs>
      {local.status === "idle" ? (
        <p className="p-2 text-sm text-muted-foreground">
          Search your music and external sources.
        </p>
      ) : (
        // Artists beside Tracks on a wide screen, stacked on a phone. There is no
        // Albums group: the API has no album search.
        <div className="grid min-h-0 flex-1 content-start gap-4 overflow-y-auto lg:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
          {fields.artist && (
            <Group label="Artists" empty={artistsEmpty}>
              {showLocal && (
                <SourceRows
                  source="My Music"
                  status={local.status}
                  onRetry={local.retry}
                >
                  {local.artists.map((artist) => (
                    <LocalArtistRow
                      key={artist.id}
                      artist={artist}
                      onSelect={onSelectArtist}
                    />
                  ))}
                </SourceRows>
              )}
              {showExternal && (
                <SourceRows
                  source="external"
                  status={external.status}
                  error={external.error}
                  onRetry={external.retry}
                >
                  {external.artists.map((artist) => (
                    <ExternalArtistRow
                      key={`${artist.provider}:${artist.externalId}`}
                      artist={artist}
                      onSelect={onSelectArtist}
                    />
                  ))}
                </SourceRows>
              )}
            </Group>
          )}
          <Group label="Tracks" empty={tracksEmpty}>
            {showLocal && (
              <SourceRows
                source="My Music"
                status={local.status}
                onRetry={local.retry}
              >
                {local.tracks.map((track) => (
                  <LocalTrackRow
                    key={track.id}
                    track={track}
                    onSelect={onSelectTrack}
                  />
                ))}
              </SourceRows>
            )}
            {showExternal && (
              <SourceRows
                source="external"
                status={external.status}
                error={external.error}
                onRetry={external.retry}
              >
                {external.tracks.map((track) => (
                  <ExternalTrackRow
                    key={`${track.provider}:${track.externalId}`}
                    track={track}
                  />
                ))}
              </SourceRows>
            )}
          </Group>
        </div>
      )}
    </div>
  )
}
