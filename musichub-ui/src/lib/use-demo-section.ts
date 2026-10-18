import { useState } from "react"

import type { SectionState } from "@/lib/artist"

export type DemoMode = "ready" | "loading" | "empty" | "error"

export const demoModes: DemoMode[] = ["ready", "loading", "empty", "error"]

export interface DemoSection<T> {
  state: SectionState<T>
  mode: DemoMode
  setMode: (mode: DemoMode) => void
  retry: () => void
}

// The demo data layer: serves fixed data, and lets the page force a section into
// each state. A real source replaces this hook with one that returns the same
// SectionState.
export function useDemoSection<T>(data: T, empty: T): DemoSection<T> {
  const [mode, setMode] = useState<DemoMode>("ready")

  const state: SectionState<T> =
    mode === "loading"
      ? { status: "loading" }
      : mode === "error"
        ? { status: "error", message: "Demo error: nothing was requested." }
        : { status: "ready", data: mode === "empty" ? empty : data }

  return { state, mode, setMode, retry: () => setMode("ready") }
}
