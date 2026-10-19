import { Check, Heart } from "lucide-react"
import { useState } from "react"

import { Button } from "@/components/ui/button"
import type { WishlistResult } from "@/lib/catalog-api"

type State = "idle" | "saving" | "wanted" | "error"

interface WishlistButtonProps {
  // Already wanted when shown (a catalog track that was wanted before).
  wanted?: boolean
  onWant: () => Promise<WishlistResult>
}

// Adds one track to the wishlist when pressed. Wanted is the user's intent: it says
// nothing about whether audio is stored.
export function WishlistButton({ wanted = false, onWant }: WishlistButtonProps) {
  const [state, setState] = useState<State>(wanted ? "wanted" : "idle")

  const press = async () => {
    setState("saving")
    try {
      await onWant()
      setState("wanted")
    } catch {
      setState("error")
    }
  }

  if (state === "wanted") {
    return (
      <Button size="sm" variant="secondary" disabled>
        <Check data-icon="inline-start" />
        Wanted
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
      <Heart data-icon="inline-start" />
      {state === "error" ? "Retry wishlist" : "Add to wishlist"}
    </Button>
  )
}
