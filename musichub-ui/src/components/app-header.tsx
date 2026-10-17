import { Database, ListMusic, Music, Settings, UserRound } from "lucide-react"

import { Button } from "@/components/ui/button"

export function AppHeader() {
  return (
    <header className="flex h-14 shrink-0 items-center gap-6 border-b px-4 lg:px-6">
      <div className="flex items-center gap-2 text-base font-semibold">
        <Music className="size-6 text-primary" aria-hidden="true" />
        MusicHub
      </div>
      <nav aria-label="Primary" className="flex items-center gap-1">
        <Button
          variant="secondary"
          size="lg"
          aria-current="page"
          className="bg-primary/15 text-primary hover:bg-primary/20"
        >
          <Database data-icon="inline-start" />
          Library
        </Button>
        <Button variant="ghost" size="lg" disabled title="Not available yet">
          <ListMusic data-icon="inline-start" />
          Playlists
        </Button>
        <Button variant="ghost" size="lg" disabled title="Not available yet">
          <UserRound data-icon="inline-start" />
          Artists
        </Button>
      </nav>
      <div className="ml-auto flex items-center gap-4">
        <Button variant="ghost" size="lg" disabled title="Not available yet">
          <Settings data-icon="inline-start" />
          <span className="hidden sm:inline">Settings</span>
        </Button>
        <span
          className="hidden items-center gap-2 text-xs text-muted-foreground opacity-50 sm:flex"
          title="Not available yet"
        >
          <span className="size-2 rounded-full bg-muted-foreground" />
          Status
        </span>
      </div>
    </header>
  )
}
