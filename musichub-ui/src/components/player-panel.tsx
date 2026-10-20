import { useState } from "react"
import {
  ListMusic,
  MoreHorizontal,
  Music,
  Pause,
  Play,
  Repeat,
  Shuffle,
  SkipBack,
  SkipForward,
} from "lucide-react"

import { CoverArt } from "@/components/cover-art"
import { Button } from "@/components/ui/button"
import { Slider } from "@/components/ui/slider"
import type { Track } from "@/data/mock-tracks"
import { formatDuration } from "@/lib/format"
import { isAudible, usePlayer, type PlaybackStatus } from "@/lib/use-player"
import { cn } from "@/lib/utils"

interface PlayerPanelProps {
  queue: Track[]
  nowPlayingId: string
}

interface NowPlayingProps {
  // The mobile Player page: a bigger cover, and a seek slider that doesn't swipe pages.
  large?: boolean
}

interface QueueListProps {
  queue: Track[]
  nowPlayingId: string
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
// file has no catalog metadata, so its filename stands in. Only play/pause and seek exist;
// the other controls are disabled placeholders.
export function NowPlaying({ large = false }: NowPlayingProps) {
  const { source, status, currentTime, duration, error, toggle, seek } =
    usePlayer()
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
          large ? "max-w-72" : "max-w-56 lg:max-w-60"
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
          disabled
          aria-label="Previous"
          title="Not available yet"
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
          disabled
          aria-label="Next"
          title="Not available yet"
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

export function QueueList({ queue, nowPlayingId }: QueueListProps) {
  return (
    <section
      aria-labelledby="queue-heading"
      className="flex min-h-0 flex-1 flex-col border-t"
    >
      <div className="flex items-center gap-2 px-4 py-3">
        <h2 id="queue-heading" className="font-semibold">
          Current Playlist
        </h2>
        <span className="text-muted-foreground">{queue.length} tracks</span>
        <div className="ml-auto flex items-center">
          <Button
            variant="ghost"
            size="icon"
            disabled
            aria-label="Queue view"
            title="Not available yet"
          >
            <ListMusic />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            disabled
            aria-label="Queue actions"
            title="Not available yet"
          >
            <MoreHorizontal />
          </Button>
        </div>
      </div>
      <ol className="min-h-48 flex-1 overflow-y-auto overscroll-y-contain px-2 pb-2 lg:min-h-0">
        {queue.map((track, index) => {
          const isCurrent = track.id === nowPlayingId
          return (
            <li
              key={track.id}
              aria-current={isCurrent ? "true" : undefined}
              className={cn(
                "flex items-center gap-3 rounded-md px-2 py-1.5",
                isCurrent && "bg-primary/20"
              )}
            >
              <span className="flex w-5 justify-center text-xs text-muted-foreground">
                {isCurrent ? (
                  <Play
                    className="size-3.5 fill-current text-primary"
                    aria-label="Now playing"
                  />
                ) : (
                  index + 1
                )}
              </span>
              <CoverArt track={track} className="size-9 text-xs" />
              <div className="flex min-w-0 flex-1 flex-col">
                <span className="truncate text-sm font-medium">
                  {track.title}
                </span>
                <span className="truncate text-xs text-muted-foreground">
                  {track.artist}
                </span>
              </div>
              <span className="text-xs text-muted-foreground">
                {formatDuration(track.durationSeconds)}
              </span>
            </li>
          )
        })}
      </ol>
    </section>
  )
}

export function PlayerPanel({ queue, nowPlayingId }: PlayerPanelProps) {
  return (
    <aside
      aria-label="Player"
      className="flex min-h-0 flex-col rounded-xl border bg-card"
    >
      <NowPlaying />
      <QueueList queue={queue} nowPlayingId={nowPlayingId} />
    </aside>
  )
}
