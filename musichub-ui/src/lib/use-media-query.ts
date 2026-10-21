import { useSyncExternalStore } from "react"

import { desktopMinRem } from "@/lib/layout"

export function useMediaQuery(query: string) {
  return useSyncExternalStore(
    (onChange) => {
      const list = window.matchMedia(query)
      list.addEventListener("change", onChange)
      return () => list.removeEventListener("change", onChange)
    },
    () => window.matchMedia(query).matches,
    () => false
  )
}

// Matches Tailwind's `lg` breakpoint, where the desktop grid takes over.
export function useIsDesktop() {
  return useMediaQuery(`(min-width: ${desktopMinRem}rem)`)
}
