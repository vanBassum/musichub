import {
  ListMusic,
  MoreHorizontal,
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
import { cn } from "@/lib/utils"

interface PlayerPanelProps {
  queue: Track[]
  nowPlayingId: string
}

// Playback doesn't exist yet: the player shows a mock queue, and every control is a
// disabled placeholder. Selecting a library row never changes what plays.
export function PlayerPanel({ queue, nowPlayingId }: PlayerPanelProps) {
  const current = queue.find((track) => track.id === nowPlayingId) ?? null

  return (
    <aside
      aria-label="Player"
      className="flex min-h-0 flex-col rounded-xl border bg-card"
    >
      {current ? (
        <div className="flex flex-col items-center gap-4 p-4 pb-5">
          <CoverArt
            track={current}
            className="w-full max-w-56 text-5xl lg:max-w-60"
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
          <div
            className="flex w-full items-center justify-between"
            title="Playback is not available yet"
          >
            <Button
              variant="ghost"
              size="icon-lg"
              disabled
              aria-label="Shuffle"
            >
              <Shuffle />
            </Button>
            <Button
              variant="ghost"
              size="icon-lg"
              disabled
              aria-label="Previous"
            >
              <SkipBack className="fill-current" />
            </Button>
            <Button
              size="icon-lg"
              disabled
              aria-label="Play"
              className="size-14 rounded-full [&_svg:not([class*='size-'])]:size-6"
            >
              <Play className="fill-current" />
            </Button>
            <Button variant="ghost" size="icon-lg" disabled aria-label="Next">
              <SkipForward className="fill-current" />
            </Button>
            <Button variant="ghost" size="icon-lg" disabled aria-label="Repeat">
              <Repeat />
            </Button>
          </div>
        </div>
      ) : (
        <p className="p-4 text-sm text-muted-foreground">Nothing is playing.</p>
      )}
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
        <ol className="min-h-48 flex-1 overflow-y-auto px-2 pb-2 lg:min-h-0">
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
    </aside>
  )
}
