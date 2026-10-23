// Which results the Search page shows and how many of each it asks the provider for.
export const categories = ["all", "albums", "tracks", "artists"] as const
export type Category = (typeof categories)[number]
export type ResultKind = Exclude<Category, "all">

export const categoryLabels: Record<Category, string> = {
  all: "All",
  albums: "Albums",
  tracks: "Tracks",
  artists: "Artists",
}

// The overview shows a few of each kind; a focused category shows a page and grows
// by a page per "Show more". The API has no offset, so more means a larger limit,
// up to the maximum its endpoint accepts for that kind.
const overviewSize = 5
const pageSize = 20
const maxLimits: Record<ResultKind, number> = {
  albums: 100,
  tracks: 20,
  artists: 20,
}

// How many results of `kind` to ask for. 0 means none: the kind isn't shown.
export function limitFor(kind: ResultKind, category: Category, pages: number) {
  if (category === "all") return overviewSize
  if (category !== kind) return 0
  return Math.min(pageSize * (pages + 1), maxLimits[kind])
}

// A full answer may have more behind it; a short one has everything.
export const canShowMore = (kind: ResultKind, count: number, limit: number) =>
  limit > 0 && limit < maxLimits[kind] && count >= limit
