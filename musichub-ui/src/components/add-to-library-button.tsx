import { Check, Plus } from "lucide-react"
import { useState } from "react"

import { Button } from "@/components/ui/button"

type State = "idle" | "saving" | "added" | "error"

interface AddToLibraryButtonProps {
  // Adds the track to the library. Absent when the track is in the library already.
  onAdd?: () => Promise<unknown>
}

// Adds one provider track to the library when pressed. Being in the library says
// nothing about whether audio is stored: a track without audio is Missing Music.
export function AddToLibraryButton({ onAdd }: AddToLibraryButtonProps) {
  const [state, setState] = useState<State>("idle")

  const press = async () => {
    if (!onAdd) return
    setState("saving")
    try {
      await onAdd()
      setState("added")
    } catch {
      setState("error")
    }
  }

  if (!onAdd || state === "added") {
    return (
      <Button size="sm" variant="secondary" disabled>
        <Check data-icon="inline-start" />
        In library
      </Button>
    )
  }
  return (
    <Button
      size="sm"
      variant="outline"
      disabled={state === "saving"}
      onClick={press}
    >
      <Plus data-icon="inline-start" />
      {state === "error" ? "Retry add to library" : "Add to library"}
    </Button>
  )
}
