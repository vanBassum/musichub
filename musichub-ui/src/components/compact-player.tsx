import { Music, Pause, Play, SkipBack, SkipForward } from "lucide-react"

import { NowPlaying, QueueList } from "@/components/player-panel"
import { Button } from "@/components/ui/button"
import {
  Sheet,
  SheetContent,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import { isAudible, usePlayer } from "@/lib/use-player"

// The persistent player below `lg`, outside the Player page: a bar above the navigation
// with the loaded file, play/pause and (from `md`) previous and next.
// Activating the file opens the full player as an overlay over the current page; closing
// it returns there. It shows only while something is loaded.
export function CompactPlayer() {
  const { source, status, hasNext, toggle, next, previous } = usePlayer()
  if (!source) return null

  const playing = isAudible(status)

  return (
    <Sheet>
      <section
        aria-label="Compact player"
        className="flex items-center gap-1 border-t bg-card p-2"
      >
        <SheetTrigger
          render={
            <Button
              variant="ghost"
              aria-label={`Open player: ${source.title}`}
              className="h-12 min-w-0 flex-1 justify-start gap-3 px-1"
            />
          }
        >
          <span className="flex size-10 shrink-0 items-center justify-center rounded-md bg-muted text-muted-foreground">
            <Music className="size-5" />
          </span>
          <span className="flex min-w-0 flex-col text-left">
            <span className="truncate text-sm font-medium">{source.title}</span>
            <span className="truncate text-xs text-muted-foreground">
              {source.subtitle ?? "Stored file"}
            </span>
          </span>
        </SheetTrigger>
        <Button
          variant="ghost"
          size="icon-lg"
          aria-label="Previous"
          onClick={previous}
          className="hidden size-10 md:inline-flex"
        >
          <SkipBack className="fill-current" />
        </Button>
        <Button
          size="icon-lg"
          disabled={status === "error"}
          aria-label={playing ? "Pause" : "Play"}
          onClick={toggle}
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
          disabled={!hasNext}
          aria-label="Next"
          onClick={next}
          className="hidden size-10 md:inline-flex"
        >
          <SkipForward className="fill-current" />
        </Button>
      </section>
      <SheetContent
        side="bottom"
        className="data-[side=bottom]:h-dvh sm:max-w-none"
      >
        <SheetTitle className="sr-only">Player</SheetTitle>
        <div className="mx-auto flex min-h-0 w-full max-w-xl flex-1 flex-col overflow-y-auto pt-10">
          <NowPlaying large />
          <QueueList />
        </div>
      </SheetContent>
    </Sheet>
  )
}
