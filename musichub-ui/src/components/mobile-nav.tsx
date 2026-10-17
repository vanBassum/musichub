import { mobilePages } from "@/components/mobile-pages"
import { Button } from "@/components/ui/button"

interface MobileNavProps {
  active: number
  onNavigate: (index: number) => void
}

export function MobileNav({ active, onNavigate }: MobileNavProps) {
  return (
    <nav aria-label="Pages" className="flex gap-1 border-t bg-card p-1.5">
      {mobilePages.map(({ label, icon: Icon }, index) => {
        const isActive = index === active
        return (
          <Button
            key={label}
            variant={isActive ? "secondary" : "ghost"}
            aria-current={isActive ? "page" : undefined}
            onClick={() => onNavigate(index)}
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
