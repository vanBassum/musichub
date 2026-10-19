import {
  ListMusic,
  MoreHorizontal,
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
import type { PlayerState } from "@/lib/use-player"
import { cn } from "@/lib/utils"

interface PlayerPanelProps {
  player: PlayerState
  className?: string
}

interface NowPlayingProps {
  player: PlayerState
  // The expanded overlay: a bigger cover.
  large?: boolean
}

interface QueueListProps {
  queue: Track[]
  nowPlayingId: string
}

// Playback doesn't exist yet: the player shows a mock queue. Play, shuffle and repeat
// only toggle local placeholder state; the rest are disabled. Selecting a library row
// never changes what plays.
export function NowPlaying({ player, large = false }: NowPlayingProps) {
  const { current, playing, shuffle, repeat } = player

  if (!current) {
    return (
      <p className="p-4 text-sm text-muted-foreground">Nothing is playing.</p>
    )
  }

  return (
    <div className="flex flex-col items-center gap-4 p-4 pb-5">
      <CoverArt
        track={current}
        className={cn(
          "w-full text-5xl",
          large ? "max-w-72" : "max-w-56 lg:max-w-60"
        )}
      />
      <div className="flex w-full flex-col items-center gap-0.5 text-center">
        <h2 className="w-full truncate text-lg font-semibold">
          {current.title}
        </h2>
        <p className="w-full truncate text-muted-foreground">
          {current.artist}
        </p>
        <p className="w-full truncate text-xs text-muted-foreground">
          {current.album}
        </p>
      </div>
      <div className="flex w-full flex-col gap-1.5">
        <Slider
          disabled
          value={[0]}
          max={current.durationSeconds}
          aria-label="Seek"
        />
        <div className="flex justify-between text-xs text-muted-foreground">
          <span>{formatDuration(0)}</span>
          <span>{formatDuration(current.durationSeconds)}</span>
        </div>
      </div>
      <div className="flex w-full items-center justify-between">
        <Button
          variant="ghost"
          size="icon-lg"
          aria-label="Shuffle"
          aria-pressed={shuffle}
          onClick={player.onToggleShuffle}
          className={cn(shuffle && "text-primary")}
        >
          <Shuffle />
        </Button>
        <Button variant="ghost" size="icon-lg" disabled aria-label="Previous">
          <SkipBack className="fill-current" />
        </Button>
        <Button
          size="icon-lg"
          aria-label={playing ? "Pause" : "Play"}
          onClick={player.onTogglePlaying}
          className="size-14 rounded-full [&_svg:not([class*='size-'])]:size-6"
        >
          {playing ? (
            <Pause className="fill-current" />
          ) : (
            <Play className="fill-current" />
          )}
        </Button>
        <Button variant="ghost" size="icon-lg" disabled aria-label="Next">
          <SkipForward className="fill-current" />
        </Button>
        <Button
          variant="ghost"
          size="icon-lg"
          aria-label="Repeat"
          aria-pressed={repeat}
          onClick={player.onToggleRepeat}
          className={cn(repeat && "text-primary")}
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

export function PlayerPanel({ player, className }: PlayerPanelProps) {
  return (
    <aside
      aria-label="Player"
      className={cn("flex min-h-0 flex-col rounded-xl border bg-card", className)}
    >
      <NowPlaying player={player} />
      <QueueList queue={player.queue} nowPlayingId={player.nowPlayingId} />
    </aside>
  )
}
