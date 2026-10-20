import {
  Database,
  FolderOpen,
  ListChecks,
  ListMusic,
  Music,
  Search,
  Settings,
  TriangleAlert,
  UserRound,
} from "lucide-react"

import { BuildInfoSheet } from "@/components/build-info-sheet"
import { Button } from "@/components/ui/button"

export type AppView = "search" | "library" | "files" | "artists" | "jobs" | "problems"

interface AppHeaderProps {
  view: AppView
  onNavigate: (view: AppView) => void
}

const activeClassName = "bg-primary/15 text-primary hover:bg-primary/20"

export function AppHeader({ view, onNavigate }: AppHeaderProps) {
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
        <Button
          variant={view === "files" ? "secondary" : "ghost"}
          size="lg"
          aria-current={view === "files" ? "page" : undefined}
          className={view === "files" ? activeClassName : undefined}
          onClick={() => onNavigate("files")}
        >
          <FolderOpen data-icon="inline-start" />
          Files
        </Button>
        <Button variant="ghost" size="lg" disabled title="Not available yet">
          <ListMusic data-icon="inline-start" />
          Playlists
        </Button>
        <Button
          variant={view === "artists" ? "secondary" : "ghost"}
          size="lg"
          aria-current={view === "artists" ? "page" : undefined}
          className={view === "artists" ? activeClassName : undefined}
          onClick={() => onNavigate("artists")}
        >
          <UserRound data-icon="inline-start" />
          Artists
        </Button>
        <Button
          variant={view === "jobs" ? "secondary" : "ghost"}
          size="lg"
          aria-current={view === "jobs" ? "page" : undefined}
          className={view === "jobs" ? activeClassName : undefined}
          onClick={() => onNavigate("jobs")}
        >
          <ListChecks data-icon="inline-start" />
          Jobs
        </Button>
        <Button
          variant={view === "problems" ? "secondary" : "ghost"}
          size="lg"
          aria-current={view === "problems" ? "page" : undefined}
          className={view === "problems" ? activeClassName : undefined}
          onClick={() => onNavigate("problems")}
        >
          <TriangleAlert data-icon="inline-start" />
          Problems
        </Button>
      </nav>
      <div className="ml-auto flex items-center gap-4">
        <BuildInfoSheet />
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
