import { useMemo, useSyncExternalStore } from "react"

import { parseLocation, type AppLocation } from "@/lib/navigation"

function subscribe(onChange: () => void) {
  window.addEventListener("hashchange", onChange)
  return () => window.removeEventListener("hashchange", onChange)
}

const getHash = () => window.location.hash

// The current location, read from the URL hash.
export function useLocation(): AppLocation {
  const hash = useSyncExternalStore(subscribe, getHash)
  return useMemo(() => parseLocation(hash), [hash])
}
