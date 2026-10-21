import { useState } from "react"
import {
  ArrowDown,
  ArrowUp,
  Music,
  Pause,
  Play,
  Repeat,
  Shuffle,
  SkipBack,
  SkipForward,
  Trash2,
  X,
} from "lucide-react"

import { Button } from "@/components/ui/button"
import { Slider } from "@/components/ui/slider"
import { formatDuration } from "@/lib/format"
import { isAudible, usePlayer, type PlaybackStatus } from "@/lib/use-player"
import { cn } from "@/lib/utils"

interface NowPlayingProps {
  // The mobile Player page: a bigger cover, and a seek slider that doesn't swipe pages.
  large?: boolean
}

interface QueueListProps {
  className?: string
}

const statusLabels: Record<PlaybackStatus, string> = {
  idle: "",
  loading: "Loading…",
  playing: "Playing",
  paused: "Paused",
  ended: "Finished",
  error: "Can't play",
}

// The shared player: one playback state (`usePlayer`) behind every presentation. A stored
// file has no catalog metadata, so its filename stands in. Previous and Next follow the
// current queue; shuffle and repeat are disabled placeholders.
export function NowPlaying({ large = false }: NowPlayingProps) {
  const {
    source,
    status,
    currentTime,
    duration,
    error,
    hasNext,
    toggle,
    seek,
    next,
    previous,
  } = usePlayer()
  const [scrub, setScrub] = useState<number | null>(null)

  if (!source) {
    return (
      <p className="p-4 text-sm text-muted-foreground">Nothing is playing.</p>
    )
  }

  const failed = status === "error"
  const playing = isAudible(status)
  const shown = scrub ?? currentTime

  return (
    <div className="flex flex-col items-center gap-4 p-4 pb-5">
      <div
        role="img"
        aria-label="No cover art"
        className={cn(
          "flex aspect-square w-full items-center justify-center rounded-md bg-muted text-muted-foreground",
          // The docked desktop player gives the cover up first on short windows.
          large
            ? "max-w-72"
            : "max-w-40 [@media(max-height:56rem)]:hidden"
        )}
      >
        <Music className="size-1/4" />
      </div>
      <div className="flex w-full flex-col items-center gap-0.5 text-center">
        <h2
          className="w-full truncate text-lg font-semibold"
          title={source.title}
        >
          {source.title}
        </h2>
        <p className="w-full truncate text-muted-foreground">
          {source.subtitle ?? "Stored file"}
        </p>
        <p
          role="status"
          className="w-full truncate text-xs text-muted-foreground"
        >
          {statusLabels[status]}
        </p>
      </div>
      {failed && (
        <p role="alert" className="text-center text-sm text-destructive">
          {error}
        </p>
      )}
      <div
        className={cn("flex w-full flex-col gap-1.5", large && "touch-none")}
      >
        <Slider
          disabled={failed || duration === 0}
          value={[Math.min(shown, duration)]}
          max={duration || 1}
          step={0.1}
          onValueChange={(value) =>
            setScrub(Array.isArray(value) ? value[0] : value)
          }
          onValueCommitted={(value) => {
            seek(Array.isArray(value) ? value[0] : value)
            setScrub(null)
          }}
          aria-label="Seek"
        />
        <div className="flex justify-between text-xs text-muted-foreground">
          <span>{formatDuration(shown)}</span>
          <span>{duration > 0 ? formatDuration(duration) : "--:--"}</span>
        </div>
      </div>
      <div className="flex w-full items-center justify-between">
        <Button
          variant="ghost"
          size="icon-lg"
          disabled
          aria-label="Shuffle"
          title="Not available yet"
        >
          <Shuffle />
        </Button>
        <Button
          variant="ghost"
          size="icon-lg"
          onClick={previous}
          aria-label="Previous"
        >
          <SkipBack className="fill-current" />
        </Button>
        <Button
          size="icon-lg"
          disabled={failed}
          onClick={toggle}
          aria-label={playing ? "Pause" : "Play"}
          className="size-14 rounded-full [&_svg:not([class*='size-'])]:size-6"
        >
          {playing ? (
            <Pause className="fill-current" />
          ) : (
            <Play className="fill-current" />
          )}
        </Button>
        <Button
          variant="ghost"
          size="icon-lg"
          disabled={!hasNext}
          onClick={next}
          aria-label="Next"
        >
          <SkipForward className="fill-current" />
        </Button>
        <Button
          variant="ghost"
          size="icon-lg"
          disabled
          aria-label="Repeat"
          title="Not available yet"
        >
          <Repeat />
        </Button>
      </div>
    </div>
  )
}

// The current playback queue: ephemeral, edited while listening. Selecting an entry plays it.
export function QueueList({ className }: QueueListProps) {
  const { queue, currentKey, playEntry, removeEntry, moveEntry, clearQueue } =
    usePlayer()

  return (
    <section
      aria-labelledby="queue-heading"
      className={cn("flex min-h-0 flex-1 flex-col border-t", className)}
    >
      <div className="flex items-center gap-2 px-4 py-3">
        <h2 id="queue-heading" className="font-semibold">
          Current Playlist
        </h2>
        <span className="text-muted-foreground">
          {queue.length} {queue.length === 1 ? "track" : "tracks"}
        </span>
        <Button
          variant="ghost"
          size="sm"
          className="ml-auto"
          disabled={queue.length === 0 || (queue.length === 1 && currentKey !== null)}
          onClick={clearQueue}
          title="Removes everything except the track that is playing"
        >
          <Trash2 />
          Clear
        </Button>
      </div>
      {queue.length === 0 && (
        <p className="px-4 pb-4 text-sm text-muted-foreground">
          The queue is empty. Add tracks from the Library.
        </p>
      )}
      <ol className="min-h-48 flex-1 overflow-y-auto overscroll-y-contain px-2 pb-2 lg:min-h-0">
        {queue.map((entry, index) => {
          const isCurrent = entry.key === currentKey
          return (
            <li
              key={entry.key}
              aria-current={isCurrent ? "true" : undefined}
              className={cn(
                "flex items-center gap-1 rounded-md px-1 py-0.5",
                isCurrent && "bg-primary/20"
              )}
            >
              <button
                type="button"
                onClick={() => playEntry(entry.key)}
                aria-label={`Play ${entry.source.title}`}
                className="flex min-w-0 flex-1 items-center gap-3 rounded-md px-1 py-1 text-left outline-none focus-visible:ring-2 focus-visible:ring-ring/40"
              >
                <span className="flex w-5 shrink-0 justify-center text-xs text-muted-foreground">
                  {isCurrent ? (
                    <Play
                      className="size-3.5 fill-current text-primary"
                      aria-label="Now playing"
                    />
                  ) : (
                    index + 1
                  )}
                </span>
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="truncate text-sm font-medium">
                    {entry.source.title}
                  </span>
                  <span className="truncate text-xs text-muted-foreground">
                    {entry.source.subtitle ?? "No artist"}
                  </span>
                </span>
              </button>
              <Button
                variant="ghost"
                size="icon-sm"
                disabled={index === 0}
                onClick={() => moveEntry(entry.key, -1)}
                aria-label={`Move ${entry.source.title} up`}
              >
                <ArrowUp />
              </Button>
              <Button
                variant="ghost"
                size="icon-sm"
                disabled={index === queue.length - 1}
                onClick={() => moveEntry(entry.key, 1)}
                aria-label={`Move ${entry.source.title} down`}
              >
                <ArrowDown />
              </Button>
              <Button
                variant="ghost"
                size="icon-sm"
                onClick={() => removeEntry(entry.key)}
                aria-label={`Remove ${entry.source.title}`}
              >
                <X />
              </Button>
            </li>
          )
        })}
      </ol>
    </section>
  )
}

export function PlayerPanel() {
  return (
    <aside
      aria-label="Player"
      className="flex min-h-0 flex-col overflow-hidden rounded-xl border bg-card"
    >
      <QueueList className="border-t-0" />
      {/* Docked at the bottom: the queue scrolls above it, the controls never leave. */}
      <div className="shrink-0 border-t">
        <NowPlaying />
      </div>
    </aside>
  )
}
