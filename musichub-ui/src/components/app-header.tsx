import {
  Database,
  Download,
  ListMusic,
  Music,
  Search,
  Settings,
} from "lucide-react"

import { BuildInfoSheet } from "@/components/build-info-sheet"
import { ProblemCountBadge } from "@/components/problem-count-badge"
import { Button } from "@/components/ui/button"

export type AppView = "search" | "library" | "downloads" | "settings"

interface AppHeaderProps {
  view: AppView
  onNavigate: (view: AppView) => void
  // The recorded problems, shown on Settings so they stay discoverable.
  problems: number
}

const activeClassName = "bg-primary/15 text-primary hover:bg-primary/20"

export function AppHeader({ view, onNavigate, problems }: AppHeaderProps) {
  return (
    <header className="flex h-14 shrink-0 items-center gap-6 border-b px-4 lg:px-6">
      <div className="flex items-center gap-2 text-base font-semibold">
        <Music className="size-6 text-primary" aria-hidden="true" />
        MusicHub
      </div>
      <nav aria-label="Primary" className="flex items-center gap-1">
        <Button
          variant={view === "search" ? "secondary" : "ghost"}
          size="lg"
          aria-current={view === "search" ? "page" : undefined}
          className={view === "search" ? activeClassName : undefined}
          onClick={() => onNavigate("search")}
        >
          <Search data-icon="inline-start" />
          Search
        </Button>
        <Button
          variant={view === "library" ? "secondary" : "ghost"}
          size="lg"
          aria-current={view === "library" ? "page" : undefined}
          className={view === "library" ? activeClassName : undefined}
          onClick={() => onNavigate("library")}
        >
          <Database data-icon="inline-start" />
          Library
        </Button>
        <Button variant="ghost" size="lg" disabled title="Not available yet">
          <ListMusic data-icon="inline-start" />
          Playlists
        </Button>
        <Button
          variant={view === "downloads" ? "secondary" : "ghost"}
          size="lg"
          aria-current={view === "downloads" ? "page" : undefined}
          className={view === "downloads" ? activeClassName : undefined}
          onClick={() => onNavigate("downloads")}
        >
          <Download data-icon="inline-start" />
          Downloads
        </Button>
      </nav>
      <div className="ml-auto flex items-center gap-4">
        <BuildInfoSheet />
        <Button
          variant={view === "settings" ? "secondary" : "ghost"}
          size="lg"
          aria-current={view === "settings" ? "page" : undefined}
          className={view === "settings" ? activeClassName : undefined}
          onClick={() => onNavigate("settings")}
        >
          <Settings data-icon="inline-start" />
          <span className="hidden sm:inline">Settings</span>
          <ProblemCountBadge count={problems} />
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
