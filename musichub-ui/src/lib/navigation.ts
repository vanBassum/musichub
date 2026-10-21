import { Download, Library, Music, Search, Settings } from "lucide-react"

import { jobFilters, type JobFilter } from "@/lib/jobs-api"
import {
  formatSettingsHash,
  parseSettingsSection,
  type SettingsSectionId,
} from "@/components/settings-sections"
import {
  formatArtistHash,
  parseArtistHash,
  type ArtistRoute,
} from "@/lib/artist-route"

// Where the app can be. Search, Library and Downloads are the music destinations,
// Settings groups the operational screens, Player is the phone's playback page, and
// Artist is a contextual page reached from Search (it has no navigation entry).
export const destinations = {
  search: { label: "Search", icon: Search },
  library: { label: "Library", icon: Library },
  downloads: { label: "Downloads", icon: Download },
  player: { label: "Player", icon: Music },
  settings: { label: "Settings", icon: Settings },
} as const

export type NavId = keyof typeof destinations
export type TabId = NavId | "artist"

// The destinations on the desktop header: the music ones, with Settings set apart.
export const primaryNav = ["search", "library", "downloads"] as const

// The phone's navigation bar: the same, plus the Player page.
export const mobileNav = [
  "search",
  "library",
  "downloads",
  "player",
  "settings",
] as const

export type PlayerView = "now-playing" | "queue"

export interface AppLocation {
  tab: TabId
  // The open Settings screen, if any.
  screen: SettingsSectionId | null
  playerView: PlayerView
  // The artist page's artist and where it was opened from; set when `tab` is "artist".
  artist: ArtistRoute | null
}

const isTab = (value: string): value is TabId =>
  value === "artist" || value in destinations


// The location lives in the URL hash (#/search, #/library, #/downloads, #/player,
// #/player/queue, #/settings, #/settings/files, #/artist/local/7), so browser back and
// shared links work. The earlier `#/more/...` links still resolve to where those
// screens went. An artist link that names no artist opens Search.
export function parseLocation(hash: string): AppLocation {
  const [tab, sub] = hash.replace(/^#\/?/, "").split("?")[0].split("/")
  const location: AppLocation = {
    tab: "search",
    screen: null,
    playerView: "now-playing",
    artist: null,
  }
  if (tab === "more") {
    if (sub === "library" || sub === "downloads") return { ...location, tab: sub }
    // The old Artists screen needs an artist that a link can't carry.
    return sub === "artists"
      ? location
      : { ...location, tab: "settings", screen: parseSettingsSection(sub) }
  }
  if (!isTab(tab)) return location
  if (tab === "artist") {
    const artist = parseArtistHash(hash)
    return artist ? { ...location, tab, artist } : location
  }
  return {
    ...location,
    tab,
    screen: tab === "settings" ? parseSettingsSection(sub) : null,
    playerView: tab === "player" && sub === "queue" ? "queue" : "now-playing",
  }
}

export function isSearchHash(hash: string): boolean {
  return parseLocation(hash).tab === "search"
}

// A search deep link is `#/search?q=<term>`.
export function parseSearchQuery(hash: string): string {
  if (!isSearchHash(hash)) return ""
  return new URLSearchParams(hash.split("?")[1] ?? "").get("q") ?? ""
}

export function formatSearchHash(query: string): string {
  const term = query.trim()
  return term ? `#/search?${new URLSearchParams({ q: term })}` : "#/search"
}

// Keeps the hash in step with the search term while Search shows, without a history
// entry.
export function syncSearchHash(query: string) {
  window.history.replaceState(null, "", formatSearchHash(query))
}

// A Jobs history deep link is `#/settings/jobs?state=failed` (or `stuck`); anything else lists all runs.
export function parseJobsFilter(hash: string): JobFilter {
  const location = parseLocation(hash)
  if (location.tab !== "settings" || location.screen !== "jobs") return "all"
  const state = new URLSearchParams(hash.split("?")[1] ?? "").get("state")
  return jobFilters.find((filter) => filter === state) ?? "all"
}

export function formatJobsHash(filter: JobFilter): string {
  const hash = formatSettingsHash("jobs")
  return filter === "all" ? hash : `${hash}?${new URLSearchParams({ state: filter })}`
}

// Keeps the hash in step with the history filter while Jobs shows, without a history entry.
export function syncJobsHash(filter: JobFilter) {
  window.history.replaceState(null, "", formatJobsHash(filter))
}

// `searchQuery` is the term Search reopens with.
export function formatLocation(
  { tab, screen, playerView, artist }: AppLocation,
  searchQuery = ""
): string {
  if (tab === "search") return formatSearchHash(searchQuery)
  if (tab === "artist" && artist) return formatArtistHash(artist)
  if (tab === "settings") return screen ? formatSettingsHash(screen) : "#/settings"
  if (tab === "player" && playerView === "queue") return "#/player/queue"
  return `#/${tab}`
}

export function locationOf(
  tab: TabId,
  screen: SettingsSectionId | null = null,
  playerView: PlayerView = "now-playing"
): AppLocation {
  return { tab, screen, playerView, artist: null }
}

// The navigation entry that shows as current. An artist page belongs to the page it
// was opened from.
export function activeNav({ tab, artist }: AppLocation): NavId {
  return tab === "artist" ? (artist?.source ?? "search") : tab
}
