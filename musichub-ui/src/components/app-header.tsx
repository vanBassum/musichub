import { CircleDot, Music2, Settings } from "lucide-react"

import { Button } from "@/components/ui/button"

export function AppHeader() {
  return (
    <header className="flex h-12 shrink-0 items-center gap-4 border-b px-4">
      <div className="flex items-center gap-2 font-semibold">
        <Music2 className="size-4" aria-hidden="true" />
        MusicHub
      </div>
      <nav aria-label="Primary" className="flex items-center gap-1">
        <Button variant="secondary" size="sm" aria-current="page">
          Library
        </Button>
        <Button variant="ghost" size="sm" disabled title="Not available yet">
          Playlists
        </Button>
      </nav>
      <div className="ml-auto flex items-center gap-2">
        <span
          className="flex items-center gap-1.5 text-xs text-muted-foreground"
          title="Placeholder"
        >
          <CircleDot className="size-3" aria-hidden="true" />
          Status
        </span>
        <Button
          variant="ghost"
          size="icon"
          disabled
          aria-label="Settings"
          title="Not available yet"
        >
          <Settings />
        </Button>
      </div>
    </header>
  )
}
