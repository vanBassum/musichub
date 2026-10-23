import type { DownloadQueueStatus } from "@/lib/downloads-api"
import { getJson, problemDetail } from "@/lib/jobs-api"

// Where a wanted track stands, as GET /api/wishlist derives it from the stored files, the download queue
// and the activity timeline. Downloaded means the bytes arrived and wait to be imported.
export type WishlistState =
  | "Missing"
  | "Searching"
  | "NotFound"
  | "Failed"
  | "Queued"
  | "Downloading"
  | "Downloaded"
  | "Available"

export const wishlistStates: WishlistState[] = [
  "Missing",
  "Searching",
  "NotFound",
  "Failed",
  "Queued",
  "Downloading",
  "Downloaded",
  "Available",
]

export interface WishlistItem {
  trackId: number
  title: string
  version: string | null
  artists: string[]
  state: WishlistState
  // Search now is offered: no file, no search running, no download under way.
  canSearch: boolean
  lastActivityAt: string | null
  lastActivity: string | null
  // The track's visible download request, which Retry and Remove act on.
  downloadRequestId: number | null
  downloadStatus: DownloadQueueStatus | null
  error: string | null
}

export interface WishlistPage {
  items: WishlistItem[]
  // The items in the filtered state; the counts cover every state.
  total: number
  counts: Record<WishlistState, number>
}

// What the Wishlist narrows to: every wanted track, or one state.
export type WishlistFilter = "all" | WishlistState

// The filter as a deep link carries it (`#/wishlist?state=notfound`).
export const formatWishlistFilter = (filter: WishlistFilter) =>
  filter.toLowerCase()

// Anything that names no state is "all".
export const parseWishlistFilter = (value: string | null): WishlistFilter =>
  wishlistStates.find((state) => state.toLowerCase() === value) ?? "all"

// A state that will still change on its own, so the page keeps polling while any row is in one.
export const isWishlistBusy = (item: WishlistItem) =>
  item.state === "Searching" ||
  item.state === "Queued" ||
  item.state === "Downloading" ||
  item.state === "Downloaded"

// The newest items: the API caps a page at 100.
const pageSize = 100

// A page of the overview and the filter it was loaded for.
export interface FilteredWishlistPage extends WishlistPage {
  filter: WishlistFilter
}

export const listWishlist = async (
  filter: WishlistFilter,
  signal?: AbortSignal
): Promise<FilteredWishlistPage> => {
  const query = new URLSearchParams({ page: "1", pageSize: String(pageSize) })
  if (filter !== "all") query.set("state", filter)
  const page = await getJson<WishlistPage>(
    `/api/wishlist?${query}`,
    "The wishlist",
    signal
  )
  return { ...page, filter }
}

// POST /api/wishlist/{trackId}/search: schedules a search through the Search for Missing Music run. 409 (a
// file, a running search or download, no provider) is thrown with the API's explanation; nothing was scheduled.
export async function searchNow(trackId: number) {
  const response = await fetch(`/api/wishlist/${trackId}/search`, {
    method: "POST",
  })
  if (response.ok) return
  throw new Error(
    (await problemDetail(response)) ??
      `The search could not be started (HTTP ${response.status}).`
  )
}

// One recorded event of a track's acquisition, as GET /api/tracks/{id}/activity returns it.
export interface TrackActivityEvent {
  id: number
  kind: string
  occurredAt: string
  summary: string
  detail: string | null
  attemptNumber: number | null
  resultCount: number | null
  plausibleCount: number | null
  eligibleCount: number | null
}

export interface TrackActivityPage {
  items: TrackActivityEvent[]
  total: number
}

// The track's timeline, oldest first. An empty one means nothing was recorded, not that data is missing.
export const getTrackActivity = (trackId: string, signal?: AbortSignal) =>
  getJson<TrackActivityPage>(
    `/api/tracks/${encodeURIComponent(trackId)}/activity?pageSize=200`,
    "The activity",
    signal
  )
