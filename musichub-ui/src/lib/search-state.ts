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

// The overview shows a few of each kind; a focused category shows a page and gets
// another page (the next offset) per "Show more".
const overviewSize = 5
const pageSize = 20

// How many results of `kind` one request asks for. 0 means none: the kind isn't shown.
export function limitFor(kind: ResultKind, category: Category) {
  if (category === "all") return overviewSize
  return category === kind ? pageSize : 0
}

// A full last page may have more behind it; a short one has everything.
export const canShowMore = (count: number, limit: number, pages: number) =>
  limit > 0 && count >= limit * (pages + 1)
