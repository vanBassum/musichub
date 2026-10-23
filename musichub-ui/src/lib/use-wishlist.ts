import { useCallback, useEffect, useRef, useState } from "react"

import { removeDownload, retryDownload } from "@/lib/downloads-api"
import {
  isWishlistBusy,
  listWishlist,
  searchNow,
  type WishlistFilter,
  type WishlistItem,
  type WishlistPage,
} from "@/lib/wishlist-api"
import { useLoader } from "@/lib/use-loader"

const pollMs = 5000

export interface WishlistModel {
  items: WishlistItem[] | null
  // The items in the filtered state, which can be more than `items` holds.
  total: number
  // How many items each state has, whatever the filter.
  counts: WishlistPage["counts"] | null
  error: string | null
  refreshing: boolean
  refresh: () => Promise<void>
  // Tracks with an action under way: their buttons are disabled, so a double click sends one call.
  busyIds: ReadonlySet<number>
  // What the last action did or why it failed; the list was reloaded, so it shows the real state next to it.
  actionError: string | null
  notice: string | null
  search: (item: WishlistItem) => Promise<void>
  retry: (item: WishlistItem) => Promise<void>
  remove: (item: WishlistItem) => Promise<void>
}

// Loads the wishlist for a filter, and polls it while a track is searching or being downloaded. Right after
// the filter changes, `items` is null until the new filter's items arrive. Each action calls the API and then
// reloads the list: the page shows what the server holds, never a guess.
export function useWishlist(filter: WishlistFilter): WishlistModel {
  const fetcher = useCallback(
    (signal: AbortSignal) => listWishlist(filter, signal),
    [filter]
  )
  const { data: loaded, error, refreshing, refresh, reload } = useLoader(fetcher)
  const data = loaded?.filter === filter ? loaded : null
  const [busyIds, setBusyIds] = useState<ReadonlySet<number>>(new Set())
  const [actionError, setActionError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const inFlight = useRef(new Set<number>())

  const active = data?.items.some(isWishlistBusy) ?? false
  useEffect(() => {
    if (!active) return
    const timer = setInterval(() => void reload(), pollMs)
    return () => clearInterval(timer)
  }, [active, reload])

  const act = useCallback(
    async (item: WishlistItem, action: () => Promise<void>, done?: string) => {
      if (inFlight.current.has(item.trackId)) return
      inFlight.current.add(item.trackId)
      setBusyIds(new Set(inFlight.current))
      setActionError(null)
      setNotice(null)
      try {
        await action()
        setNotice(done ?? null)
      } catch (failure) {
        setActionError(
          failure instanceof Error ? failure.message : "The action failed."
        )
      } finally {
        await reload()
        inFlight.current.delete(item.trackId)
        setBusyIds(new Set(inFlight.current))
      }
    },
    [reload]
  )

  // Retry and Remove act on the track's download request; the rows only offer them when there is one.
  const search = useCallback(
    (item: WishlistItem) =>
      act(
        item,
        () => searchNow(item.trackId),
        `Search scheduled for ${item.title}.`
      ),
    [act]
  )
  const retry = useCallback(
    (item: WishlistItem) =>
      act(item, () => retryDownload(item.downloadRequestId ?? 0)),
    [act]
  )
  const remove = useCallback(
    (item: WishlistItem) =>
      act(item, () => removeDownload(item.downloadRequestId ?? 0)),
    [act]
  )

  return {
    items: data?.items ?? null,
    total: data?.total ?? 0,
    counts: data?.counts ?? null,
    error,
    refreshing,
    refresh,
    busyIds,
    actionError,
    notice,
    search,
    retry,
    remove,
  }
}
