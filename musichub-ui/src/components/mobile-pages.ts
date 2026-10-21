import {
  Download,
  Ellipsis,
  FolderOpen,
  Library,
  ListChecks,
  Music,
  Search,
  TriangleAlert,
} from "lucide-react"

// The phone's primary destinations, in nav order.
export const mobileTabs = [
  { id: "search", label: "Search", icon: Search },
  { id: "player", label: "Player", icon: Music },
  { id: "more", label: "More", icon: Ellipsis },
] as const

export type MobileTabId = (typeof mobileTabs)[number]["id"]

// The screens listed under More.
export const moreScreens = [
  { id: "library", label: "Library", icon: Library },
  { id: "files", label: "Files", icon: FolderOpen },
  { id: "jobs", label: "Jobs", icon: ListChecks },
  { id: "downloads", label: "Downloads", icon: Download },
  { id: "problems", label: "Problems", icon: TriangleAlert },
] as const

export type MoreScreenId = (typeof moreScreens)[number]["id"]

export type PlayerView = "now-playing" | "queue"

export interface MobileLocation {
  tab: MobileTabId
  // The open More screen, if any.
  screen: MoreScreenId | null
  playerView: PlayerView
}

// The location lives in the URL hash (#/search, #/player, #/player/queue, #/more,
// #/more/files), so browser back and shared links work. The old #/more/artists link
// opens Search, since artists are reached from there now.
export function parseLocation(hash: string): MobileLocation {
  const [tab, sub] = hash.replace(/^#\/?/, "").split("?")[0].split("/")
  const screen = moreScreens.find((s) => s.id === sub)?.id ?? null
  const legacyArtists = tab === "more" && sub === "artists"
  return {
    tab: legacyArtists
      ? "search"
      : (mobileTabs.find((t) => t.id === tab)?.id ?? "search"),
    screen: tab === "more" ? screen : null,
    playerView: tab === "player" && sub === "queue" ? "queue" : "now-playing",
  }
}

export function isSearchHash(hash: string): boolean {
  return hash.startsWith("#/search")
}

// A search deep link is `#/search?q=<term>`, on desktop and phone alike.
export function parseSearchQuery(hash: string): string {
  const { tab } = parseLocation(hash)
  if (tab !== "search") return ""
  return new URLSearchParams(hash.split("?")[1] ?? "").get("q") ?? ""
}

export function formatSearchHash(query: string): string {
  const term = query.trim()
  return term ? `#/search?${new URLSearchParams({ q: term })}` : "#/search"
}

// Phone: go to the Search tab, keeping the term in the hash (a history entry).
export function openSearchHash(query: string) {
  window.location.hash = formatSearchHash(query)
}

// Keeps the hash in step with the search term, without a history entry. The desktop
// owns the hash while its Search view shows and clears it elsewhere; the phone only
// updates it while its location is Search.
export function syncSearchHash(
  query: string,
  isDesktop: boolean,
  desktopOnSearch: boolean
) {
  const onHash = isSearchHash(window.location.hash)
  if (isDesktop ? desktopOnSearch : onHash) {
    window.history.replaceState(null, "", formatSearchHash(query))
  } else if (isDesktop && onHash) {
    window.history.replaceState(null, "", window.location.pathname)
  }
}

export function formatLocation({
  tab,
  screen,
  playerView,
}: MobileLocation): string {
  if (tab === "more") return screen ? `#/more/${screen}` : "#/more"
  if (tab === "player") {
    return playerView === "queue" ? "#/player/queue" : "#/player"
  }
  return "#/search"
}
