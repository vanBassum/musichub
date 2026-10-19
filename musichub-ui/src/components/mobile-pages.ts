import {
  FolderOpen,
  Library,
  ListChecks,
  ListMusic,
  Music,
  UserRound,
} from "lucide-react"

// The order of the mobile pages, left to right: the strip and the nav both follow it.
export const mobilePages = [
  { id: "library", label: "Library", icon: Library },
  { id: "player", label: "Player", icon: Music },
  { id: "queue", label: "Queue", icon: ListMusic },
  { id: "files", label: "Files", icon: FolderOpen },
  { id: "artists", label: "Artists", icon: UserRound },
  { id: "jobs", label: "Jobs", icon: ListChecks },
] as const

export type MobilePageId = (typeof mobilePages)[number]["id"]

export function mobilePageIndex(id: MobilePageId) {
  return mobilePages.findIndex((page) => page.id === id)
}
