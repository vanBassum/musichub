// What the artist detail page shows. The page only knows these shapes: a real
// source later replaces the demo data layer without touching the layout.
export type Availability = "available" | "missing" | "external"

export interface Artist {
  name: string
  // Where the artist comes from: the local catalog or an external source.
  origin: "local" | "external"
  inCatalog: boolean
  genres: string[]
  description: string
}

export interface PopularTrack {
  id: string
  title: string
  album: string
  year: number
  availability: Availability
}

export interface ArtistAlbum {
  id: string
  title: string
  year: number
  availability: Availability
}

// One section's data: each section loads, fails and empties on its own.
export type SectionState<T> =
  | { status: "loading" }
  | { status: "error"; message: string }
  | { status: "ready"; data: T }

export const availabilityLabels: Record<Availability, string> = {
  available: "Available",
  missing: "Missing",
  external: "External",
}
