import { mobileTabs, type MobileTabId } from "@/components/mobile-pages"
import { Button } from "@/components/ui/button"

interface MobileNavProps {
  active: MobileTabId
  onNavigate: (tab: MobileTabId) => void
}

export function MobileNav({ active, onNavigate }: MobileNavProps) {
  return (
    <nav aria-label="Pages" className="flex gap-1 border-t bg-card p-1.5">
      {mobileTabs.map(({ id, label, icon: Icon }) => {
        const isActive = id === active
        return (
          <Button
            key={id}
            variant={isActive ? "secondary" : "ghost"}
            aria-current={isActive ? "page" : undefined}
            onClick={() => onNavigate(id)}
            className="h-12 flex-1 flex-col gap-0.5 text-xs data-[active=true]:text-primary"
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
