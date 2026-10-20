import {
  Ellipsis,
  FolderOpen,
  Library,
  ListChecks,
  Music,
  Search,
  UserRound,
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
  { id: "artists", label: "Artists", icon: UserRound },
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
// #/more/files), so browser back and shared links work.
export function parseLocation(hash: string): MobileLocation {
  const [tab, sub] = hash.replace(/^#\/?/, "").split("/")
  const screen = moreScreens.find((s) => s.id === sub)?.id ?? null
  return {
    tab: mobileTabs.find((t) => t.id === tab)?.id ?? "search",
    screen: tab === "more" ? screen : null,
    playerView: tab === "player" && sub === "queue" ? "queue" : "now-playing",
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
