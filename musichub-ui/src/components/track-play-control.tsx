import { Pause, Play } from "lucide-react"

import { Button } from "@/components/ui/button"
import { trackTitle, type CatalogTrack } from "@/lib/catalog-api"
import { isPlayable, usePlayer } from "@/lib/use-player"

// Plays a catalog track through its own linked Stored file in the shared player; the track
// that is loaded toggles pause. Missing Music has no file, so its button is disabled and
// never starts a stream.
export function TrackPlayControl({ track }: { track: CatalogTrack }) {
  const { source, status, playTrack, toggle } = usePlayer()
  const playable = isPlayable(track)
  const current =
    playable && source?.id === String(track.audioFileId) && status !== "error"
  const playing = current && (status === "playing" || status === "loading")
  const label = `${playing ? "Pause" : "Play"} ${trackTitle(track)}`

  return (
    <Button
      variant="ghost"
      size="icon-sm"
      disabled={!playable}
      aria-label={playable ? label : `${label}: no stored audio`}
      title={playable ? undefined : "No stored audio"}
      onClick={(event) => {
        // The row selects the track on click; playing must not also open its details.
        event.stopPropagation()
        if (!playable) return
        if (current) toggle()
        else playTrack(track)
      }}
      onKeyDown={(event) => event.stopPropagation()}
    >
      {playing ? <Pause /> : <Play />}
    </Button>
  )
}
