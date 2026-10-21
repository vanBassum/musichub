import { useMemo, useSyncExternalStore } from "react"

import { parseLocation, type AppLocation } from "@/lib/navigation"

function subscribe(onChange: () => void) {
  window.addEventListener("hashchange", onChange)
  return () => window.removeEventListener("hashchange", onChange)
}

const getHash = () => window.location.hash

// The current URL hash, which carries the location and its deep-link state.
export const useHash = () => useSyncExternalStore(subscribe, getHash)

// The current location, read from the URL hash.
export function useLocation(): AppLocation {
  const hash = useHash()
  return useMemo(() => parseLocation(hash), [hash])
}
