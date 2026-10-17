import { Library, ListMusic, Music } from "lucide-react"

// The order of the mobile pages, left to right: the strip and the nav both follow it.
export const mobilePages = [
  { id: "library", label: "Library", icon: Library },
  { id: "player", label: "Player", icon: Music },
  { id: "queue", label: "Queue", icon: ListMusic },
] as const

export type MobilePageId = (typeof mobilePages)[number]["id"]

export function mobilePageIndex(id: MobilePageId) {
  return mobilePages.findIndex((page) => page.id === id)
}
