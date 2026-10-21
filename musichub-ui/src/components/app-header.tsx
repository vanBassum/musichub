import { Music } from "lucide-react"

import { BuildInfoSheet } from "@/components/build-info-sheet"
import { ProblemCountBadge } from "@/components/problem-count-badge"
import { Button } from "@/components/ui/button"
import { destinations, primaryNav, type NavId } from "@/lib/navigation"

interface AppHeaderProps {
  active: NavId
  onNavigate: (id: NavId) => void
  // The recorded problems, shown on Settings so they stay discoverable.
  problems: number
}

const activeClassName = "bg-primary/15 text-primary hover:bg-primary/20"

function NavButton({
  id,
  active,
  onNavigate,
  hideLabelOnNarrow,
  problems,
}: AppHeaderProps & { id: NavId; hideLabelOnNarrow?: boolean }) {
  const { label, icon: Icon } = destinations[id]
  const isActive = id === active
  return (
    <Button
      variant={isActive ? "secondary" : "ghost"}
      size="lg"
      aria-current={isActive ? "page" : undefined}
      className={isActive ? activeClassName : undefined}
      onClick={() => onNavigate(id)}
    >
      <Icon data-icon="inline-start" />
      <span className={hideLabelOnNarrow ? "hidden sm:inline" : undefined}>
        {label}
      </span>
      {id === "settings" && <ProblemCountBadge count={problems} />}
    </Button>
  )
}

export function AppHeader({ active, onNavigate, problems }: AppHeaderProps) {
  return (
    <header className="flex h-14 shrink-0 items-center gap-6 border-b px-4 lg:px-6">
      <div className="flex items-center gap-2 text-base font-semibold">
        <Music className="size-6 text-primary" aria-hidden="true" />
        MusicHub
      </div>
      <nav aria-label="Primary" className="flex items-center gap-1">
        {primaryNav.map((id) => (
          <NavButton
            key={id}
            id={id}
            active={active}
            onNavigate={onNavigate}
            problems={problems}
          />
        ))}
      </nav>
      <div className="ml-auto flex items-center gap-4">
        <BuildInfoSheet />
        <nav aria-label="Management">
          <NavButton
            id="settings"
            active={active}
            onNavigate={onNavigate}
            problems={problems}
            hideLabelOnNarrow
          />
        </nav>
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
