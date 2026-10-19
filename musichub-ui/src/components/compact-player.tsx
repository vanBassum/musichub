import { Pause, Play, SkipBack, SkipForward } from "lucide-react"

import { CoverArt } from "@/components/cover-art"
import { NowPlaying, QueueList } from "@/components/player-panel"
import { Button } from "@/components/ui/button"
import {
  Sheet,
  SheetContent,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import type { PlayerState } from "@/lib/use-player"

interface CompactPlayerProps {
  player: PlayerState
}

// The persistent player below `lg`: a bar above the page navigation, with the current
// track and the basic controls (previous and next from `md` up). Activating the track
// opens the full player as an overlay over the current page; closing it returns there.
export function CompactPlayer({ player }: CompactPlayerProps) {
  const { current, playing } = player
  if (!current) return null

  return (
    <Sheet>
      <section
        aria-label="Player"
        className="flex items-center gap-1 border-t bg-card p-2"
      >
        <SheetTrigger
          render={
            <Button
              variant="ghost"
              aria-label={`Open player: ${current.title} by ${current.artist}`}
              className="h-12 min-w-0 flex-1 justify-start gap-3 px-1"
            />
          }
        >
          <CoverArt track={current} className="size-10 text-xs" />
          <span className="flex min-w-0 flex-col text-left">
            <span className="truncate text-sm font-medium">
              {current.title}
            </span>
            <span className="truncate text-xs text-muted-foreground">
              {current.artist}
            </span>
          </span>
        </SheetTrigger>
        <Button
          variant="ghost"
          size="icon-lg"
          disabled
          aria-label="Previous"
          className="hidden size-10 md:inline-flex"
        >
          <SkipBack className="fill-current" />
        </Button>
        <Button
          size="icon-lg"
          aria-label={playing ? "Pause" : "Play"}
          onClick={player.onTogglePlaying}
          className="size-10 rounded-full"
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
          className="hidden size-10 md:inline-flex"
        >
          <SkipForward className="fill-current" />
        </Button>
      </section>
      <SheetContent side="bottom" className="data-[side=bottom]:h-dvh sm:max-w-none">
        <SheetTitle className="sr-only">Player</SheetTitle>
        <div className="mx-auto flex min-h-0 w-full max-w-xl flex-1 flex-col overflow-y-auto pt-10">
          <NowPlaying player={player} large />
          <QueueList queue={player.queue} nowPlayingId={player.nowPlayingId} />
        </div>
      </SheetContent>
    </Sheet>
  )
}
