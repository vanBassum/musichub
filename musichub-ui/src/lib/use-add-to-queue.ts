import { useCallback } from "react"
import { toast } from "sonner"

import { trackTitle } from "@/lib/catalog-api"
import { usePlayer, type PlayableTrack } from "@/lib/use-player"

// The one way a track is added to the current queue (the add button, and drag and drop):
// it is appended at once, and when the file was already queued a nonblocking toast offers
// Undo, which removes only the entry this add created.
export function useAddToQueue() {
  const { enqueue, removeEntry } = usePlayer()

  return useCallback(
    (track: PlayableTrack) => {
      const { key, duplicate } = enqueue(track)
      if (!duplicate) return
      toast.warning(`${trackTitle(track)} is already in the queue`, {
        description: "It was added again.",
        action: { label: "Undo", onClick: () => removeEntry(key) },
      })
    },
    [enqueue, removeEntry]
  )
}
