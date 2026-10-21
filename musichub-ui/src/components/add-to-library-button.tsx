import { Check, Plus } from "lucide-react"
import { useState } from "react"

import { Button } from "@/components/ui/button"

type State = "idle" | "saving" | "added" | "error"

interface AddToLibraryButtonProps {
  // Adds the track to the library. Absent when the track is in the library already.
  onAdd?: () => Promise<unknown>
}

// An icon on a phone (the label stays for screen readers and as a tooltip), icon and
// label from the sm breakpoint up.
const compact = "max-sm:size-9 max-sm:px-0 max-sm:[&_svg]:size-4"
const labelClass = "max-sm:sr-only"

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
      <Button
        size="sm"
        variant="secondary"
        disabled
        className={compact}
        title="In library"
      >
        <Check data-icon="inline-start" />
        <span className={labelClass}>In library</span>
      </Button>
    )
  }
  const label = state === "error" ? "Retry add to library" : "Add to library"
  return (
    <Button
      size="sm"
      variant="outline"
      disabled={state === "saving"}
      onClick={press}
      className={compact}
      title={label}
    >
      <Plus data-icon="inline-start" />
      <span className={labelClass}>{label}</span>
    </Button>
  )
}
