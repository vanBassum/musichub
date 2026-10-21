import { ListPlus, Pause, Play } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { trackTitle, type CatalogTrack } from "@/lib/catalog-api"
import { isAudible, isLoaded, isPlayable, usePlayer } from "@/lib/use-player"

// Plays a catalog track through its own linked Stored file in the shared player, and puts
// it in the current queue: the button plays it now (the loaded track toggles pause), the
// menu also plays it next or adds it to the end. Missing Music has no file, so both are
// disabled and never start a stream or enter the queue.
export function TrackPlayControl({ track }: { track: CatalogTrack }) {
  const { source, status, playNow, playNext, enqueue, toggle } = usePlayer()
  const playable = isPlayable(track)
  const current = playable && isLoaded(source, status, String(track.audioFileId))
  const playing = current && isAudible(status)
  const title = trackTitle(track)
  const label = `${playing ? "Pause" : "Play"} ${title}`

  return (
    // The row selects the track on click; neither control may also open its details.
    <div
      className="flex items-center justify-end"
      onClick={(event) => event.stopPropagation()}
      onKeyDown={(event) => event.stopPropagation()}
    >
      <Button
        variant="ghost"
        size="icon-sm"
        disabled={!playable}
        aria-label={playable ? label : `${label}: no stored audio`}
        title={playable ? undefined : "No stored audio"}
        onClick={() => {
          if (!playable) return
          if (current) toggle()
          else playNow(track)
        }}
      >
        {playing ? <Pause /> : <Play />}
      </Button>
      <DropdownMenu>
        <DropdownMenuTrigger
          render={
            <Button
              variant="ghost"
              size="icon-sm"
              disabled={!playable}
              aria-label={`Queue options for ${title}`}
              title={playable ? undefined : "No stored audio"}
            />
          }
        >
          <ListPlus />
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          {playable && (
            <>
              <DropdownMenuItem onClick={() => playNow(track)}>
                Play now
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => playNext(track)}>
                Play next
              </DropdownMenuItem>
              <DropdownMenuItem onClick={() => enqueue(track)}>
                Add to queue
              </DropdownMenuItem>
            </>
          )}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  )
}
