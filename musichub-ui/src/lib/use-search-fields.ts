import { useCallback, useState } from "react"

import type { SearchFields } from "@/lib/catalog-api"

const storageKey = "musichub.search-fields"
const defaults: SearchFields = { title: true, artist: true }

// Only this browser keeps the choice. Unavailable or corrupt storage gives the defaults.
function load(): SearchFields {
  try {
    const value: unknown = JSON.parse(
      localStorage.getItem(storageKey) ?? "null"
    )
    if (
      typeof value === "object" &&
      value !== null &&
      "title" in value &&
      "artist" in value &&
      typeof value.title === "boolean" &&
      typeof value.artist === "boolean" &&
      (value.title || value.artist)
    ) {
      return { title: value.title, artist: value.artist }
    }
  } catch {
    // Fall through to the defaults.
  }
  return defaults
}

export function useSearchFields() {
  const [fields, setFields] = useState(load)

  // At least one field stays on: turning off the last one is ignored.
  const setField = useCallback(
    (field: keyof SearchFields, enabled: boolean) => {
      setFields((previous) => {
        const next = { ...previous, [field]: enabled }
        if (!next.title && !next.artist) return previous
        try {
          localStorage.setItem(storageKey, JSON.stringify(next))
        } catch {
          // The choice then lasts until the page is closed.
        }
        return next
      })
    },
    []
  )

  return { fields, setField }
}
