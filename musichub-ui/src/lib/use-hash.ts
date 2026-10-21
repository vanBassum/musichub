import { useSyncExternalStore } from "react"

function subscribe(onChange: () => void) {
  window.addEventListener("hashchange", onChange)
  return () => window.removeEventListener("hashchange", onChange)
}

const getHash = () => window.location.hash

// The current URL hash, which carries the location (search, phone pages, artist).
export function useHash() {
  return useSyncExternalStore(subscribe, getHash)
}
