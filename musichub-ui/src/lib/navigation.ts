import {
  Download,
  FolderOpen,
  Library,
  ListChecks,
  Music,
  Search,
  Settings,
} from "lucide-react"

import { jobFilters, type JobFilter } from "@/lib/jobs-api"
import {
  formatArtistHash,
  parseArtistHash,
  type ArtistRoute,
} from "@/lib/artist-route"
import type { AudioFilter } from "@/lib/catalog-api"
import {
  formatDownloadStatus,
  parseDownloadStatus,
  type DownloadStatusFilter,
} from "@/lib/downloads-api"

// Where the app can be. Search, Library, Downloads, Files and Jobs are the working
// destinations, Settings holds configuration, Player is the phone's playback page, and
// Artist is a contextual page reached from Search (it has no navigation entry).
export const destinations = {
  search: { label: "Search", icon: Search },
  library: { label: "Library", icon: Library },
  downloads: { label: "Downloads", icon: Download },
  files: { label: "Files", icon: FolderOpen },
  jobs: { label: "Jobs", icon: ListChecks },
  player: { label: "Player", icon: Music },
  settings: { label: "Settings", icon: Settings },
} as const

export type NavId = keyof typeof destinations
export type TabId = NavId | "artist"

// The destinations in the desktop sidebar.
export const primaryNav = [
  "search",
  "library",
  "downloads",
  "files",
  "jobs",
  "settings",
] as const

// The phone's navigation bar: the music destinations and the Player page, then a menu
// with the rest.
export const mobileNav = ["search", "library", "downloads", "player"] as const
export const mobileMenuNav = ["files", "jobs", "settings"] as const

export type PlayerView = "now-playing" | "queue"

export interface AppLocation {
  tab: TabId
  playerView: PlayerView
  // The artist page's artist and where it was opened from; set when `tab` is "artist".
  artist: ArtistRoute | null
  // The Library's wanted/missing filter, and the Downloads status filter.
  audio: AudioFilter
  downloadStatus: DownloadStatusFilter
}

const isTab = (value: string): value is TabId =>
  value === "artist" || value in destinations

// Files and Jobs used to be Settings screens (`#/settings/files`), and everything used
// to sit under `#/more`. The old Artists screen needs an artist that a link can't carry.
function legacyTab(first: "more" | "settings", sub: string | undefined) {
  if (sub === "files" || sub === "jobs") return sub
  if (first === "more" && (sub === "library" || sub === "downloads")) return sub
  return first === "more" && sub === "artists" ? "search" : "settings"
}

// The location lives in the URL hash (#/search, #/library, #/downloads, #/files,
// #/jobs, #/player, #/player/queue, #/settings, #/artist/local/7), so browser back and
// shared links work. The earlier `#/settings/files`, `#/settings/jobs` and `#/more/...`
// links still resolve to where those screens went. An artist link that names no artist
// opens Search.
export function parseLocation(hash: string): AppLocation {
  const [first, sub] = hash.replace(/^#\/?/, "").split("?")[0].split("/")
  const params = new URLSearchParams(hash.split("?")[1] ?? "")
  const location: AppLocation = {
    tab: "search",
    playerView: "now-playing",
    artist: null,
    audio: "all",
    downloadStatus: "all",
  }
  const tab =
    first === "more" || first === "settings" ? legacyTab(first, sub) : first
  if (!isTab(tab)) return location
  if (tab === "artist") {
    const artist = parseArtistHash(hash)
    return artist ? { ...location, tab, artist } : location
  }
  return {
    ...location,
    tab,
    playerView: tab === "player" && sub === "queue" ? "queue" : "now-playing",
    audio: tab === "library" ? parseAudio(params.get("audio")) : "all",
    downloadStatus:
      tab === "downloads" ? parseDownloadStatus(params.get("status")) : "all",
  }
}

const parseAudio = (value: string | null): AudioFilter =>
  value === "with" || value === "without" ? value : "all"

const withQuery = (hash: string, name: string, value: string) =>
  value === "all" ? hash : `${hash}?${new URLSearchParams({ [name]: value })}`

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

// A Jobs history deep link is `#/jobs?state=failed` (or `stuck`); anything else lists all runs.
export function parseJobsFilter(hash: string): JobFilter {
  if (parseLocation(hash).tab !== "jobs") return "all"
  const state = new URLSearchParams(hash.split("?")[1] ?? "").get("state")
  return jobFilters.find((filter) => filter === state) ?? "all"
}

export function formatJobsHash(filter: JobFilter): string {
  return withQuery("#/jobs", "state", filter)
}

// Keeps the hash in step with the history filter while Jobs shows, without a history entry.
export function syncJobsHash(filter: JobFilter) {
  window.history.replaceState(null, "", formatJobsHash(filter))
}

// `searchQuery` is the term Search reopens with. Library and Downloads carry their
// filter (`#/library?audio=without`, `#/downloads?status=failed`).
export function formatLocation(
  { tab, playerView, artist, audio, downloadStatus }: AppLocation,
  searchQuery = ""
): string {
  if (tab === "search") return formatSearchHash(searchQuery)
  if (tab === "artist" && artist) return formatArtistHash(artist)
  if (tab === "player" && playerView === "queue") return "#/player/queue"
  if (tab === "library") return withQuery("#/library", "audio", audio)
  if (tab === "downloads")
    return withQuery("#/downloads", "status", formatDownloadStatus(downloadStatus))
  return `#/${tab}`
}

export function locationOf(
  tab: TabId,
  playerView: PlayerView = "now-playing"
): AppLocation {
  return {
    tab,
    playerView,
    artist: null,
    audio: "all",
    downloadStatus: "all",
  }
}

// The navigation entry that shows as current. An artist page belongs to the page it
// was opened from.
export function activeNav({ tab, artist }: AppLocation): NavId {
  return tab === "artist" ? (artist?.source ?? "search") : tab
}
