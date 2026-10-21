import { Button } from "@/components/ui/button"
import { destinations, mobileNav, type NavId } from "@/lib/navigation"

interface MobileNavProps {
  active: NavId
  onNavigate: (id: NavId) => void
}

export function MobileNav({ active, onNavigate }: MobileNavProps) {
  return (
    <nav aria-label="Pages" className="flex gap-1 border-t bg-card p-1.5">
      {mobileNav.map((id) => {
        const { label, icon: Icon } = destinations[id]
        const isActive = id === active
        return (
          <Button
            key={id}
            variant={isActive ? "secondary" : "ghost"}
            aria-current={isActive ? "page" : undefined}
            onClick={() => onNavigate(id)}
            className="h-12 min-w-0 flex-1 flex-col gap-0.5 px-1 text-xs data-[active=true]:text-primary"
            data-active={isActive}
          >
            <Icon />
            {label}
          </Button>
        )
      })}
    </nav>
  )
}
