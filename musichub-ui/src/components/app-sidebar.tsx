import { Music } from "lucide-react"

import { BuildInfoSheet } from "@/components/build-info-sheet"
import { Button } from "@/components/ui/button"
import { destinations, primaryNav, type NavId } from "@/lib/navigation"

interface AppSidebarProps {
  active: NavId
  onNavigate: (id: NavId) => void
}

const activeClassName = "bg-primary/15 text-primary hover:bg-primary/20"

// The desktop's primary navigation: always visible, never collapsible.
export function AppSidebar({ active, onNavigate }: AppSidebarProps) {
  return (
    <aside className="flex w-56 shrink-0 flex-col gap-4 border-r p-3">
      <div className="flex h-10 items-center gap-2 px-2 text-base font-semibold">
        <Music className="size-6 text-primary" aria-hidden="true" />
        MusicHub
      </div>
      <nav aria-label="Primary" className="flex flex-1 flex-col gap-1">
        {primaryNav.map((id) => {
          const { label, icon: Icon } = destinations[id]
          const isActive = id === active
          return (
            <Button
              key={id}
              variant={isActive ? "secondary" : "ghost"}
              size="lg"
              aria-current={isActive ? "page" : undefined}
              className={`justify-start ${isActive ? activeClassName : ""}`}
              onClick={() => onNavigate(id)}
            >
              <Icon data-icon="inline-start" />
              {label}
            </Button>
          )
        })}
      </nav>
      <div className="flex items-center gap-2 px-1">
        <BuildInfoSheet />
        <span
          className="flex items-center gap-2 text-xs text-muted-foreground opacity-50"
          title="Not available yet"
        >
          <span className="size-2 rounded-full bg-muted-foreground" />
          Status
        </span>
      </div>
    </aside>
  )
}
