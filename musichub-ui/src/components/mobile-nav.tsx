import { Menu } from "lucide-react"
import { useState } from "react"

import { Button } from "@/components/ui/button"
import {
  Sheet,
  SheetContent,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import {
  destinations,
  mobileMenuNav,
  mobileNav,
  type NavId,
} from "@/lib/navigation"

interface MobileNavProps {
  active: NavId
  onNavigate: (id: NavId) => void
}

const itemClassName =
  "h-12 min-w-0 flex-1 flex-col gap-0.5 px-1 text-xs data-[active=true]:text-primary"

// The phone's navigation: a bar with the music destinations and the Player page, and a
// Menu that opens the rest (Files, Jobs, Settings).
export function MobileNav({ active, onNavigate }: MobileNavProps) {
  const [menuOpen, setMenuOpen] = useState(false)
  const inMenu = mobileMenuNav.some((id) => id === active)

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
            className={itemClassName}
            data-active={isActive}
          >
            <Icon />
            {label}
          </Button>
        )
      })}
      <Sheet open={menuOpen} onOpenChange={setMenuOpen}>
        <SheetTrigger
          render={
            <Button
              variant={inMenu ? "secondary" : "ghost"}
              className={itemClassName}
              data-active={inMenu}
            />
          }
        >
          <Menu />
          Menu
        </SheetTrigger>
        <SheetContent side="bottom">
          <SheetTitle className="px-4 pt-4">Menu</SheetTitle>
          <nav aria-label="More pages" className="flex flex-col gap-1 p-3 pt-0">
            {mobileMenuNav.map((id) => {
              const { label, icon: Icon } = destinations[id]
              const isActive = id === active
              return (
                <Button
                  key={id}
                  variant={isActive ? "secondary" : "ghost"}
                  size="lg"
                  aria-current={isActive ? "page" : undefined}
                  onClick={() => {
                    setMenuOpen(false)
                    onNavigate(id)
                  }}
                  className="justify-start data-[active=true]:text-primary"
                  data-active={isActive}
                >
                  <Icon data-icon="inline-start" />
                  {label}
                </Button>
              )
            })}
          </nav>
        </SheetContent>
      </Sheet>
    </nav>
  )
}
