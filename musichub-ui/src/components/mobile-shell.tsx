import { SlidersHorizontal } from "lucide-react"
import {
  useCallback,
  useLayoutEffect,
  useRef,
  useState,
  type ReactNode,
} from "react"

import { ArtistPage } from "@/components/artist-page"
import { BuildInfoSheet } from "@/components/build-info-sheet"
import { CompactPlayer } from "@/components/compact-player"
import { FilesView } from "@/components/files-view"
import { GlobalSearch } from "@/components/global-search"
import { MobileNav } from "@/components/mobile-nav"
import { mobilePageIndex } from "@/components/mobile-pages"
import { Button } from "@/components/ui/button"
import {
  Sheet,
  SheetContent,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import type { FilesState } from "@/lib/use-files"
import type { PlayerState } from "@/lib/use-player"
import { cn } from "@/lib/utils"

interface MobileShellProps {
  // The catalog's track count, once known.
  total: number | undefined
  filters: ReactNode
  table: ReactNode
  player: PlayerState
  filesState: FilesState
  onSelectTrack: (trackId: number) => void
}

const libraryPage = mobilePageIndex("library")
const filesPage = mobilePageIndex("files")
const artistsPage = mobilePageIndex("artists")

const pageClassName =
  "flex h-full w-full shrink-0 snap-center snap-always flex-col overflow-hidden"

// Below `lg` the Library, Files and Artists views become full-width pages in a
// horizontal scroll-snap strip, opening on the Library. The compact player sits between
// the strip and the page navigation. Pages that aren't showing are inert, so keyboard
// focus can't land in them.
export function MobileShell({
  total,
  filters,
  table,
  player,
  filesState,
  onSelectTrack,
}: MobileShellProps) {
  const strip = useRef<HTMLDivElement>(null)
  const [active, setActive] = useState(libraryPage)

  useLayoutEffect(() => {
    const element = strip.current
    if (!element) return
    element.scrollTo({ left: libraryPage * element.clientWidth })

    // A rotation changes the page width: stay on the page that is showing.
    let page = libraryPage
    const onResize = () =>
      element.scrollTo({ left: page * element.clientWidth })
    const onScroll = () => {
      page = Math.round(element.scrollLeft / element.clientWidth)
      setActive(page)
    }
    window.addEventListener("resize", onResize)
    element.addEventListener("scroll", onScroll, { passive: true })
    return () => {
      window.removeEventListener("resize", onResize)
      element.removeEventListener("scroll", onScroll)
    }
  }, [])

  const navigate = useCallback((index: number) => {
    const element = strip.current
    if (!element) return
    const reduceMotion = window.matchMedia(
      "(prefers-reduced-motion: reduce)"
    ).matches
    element.scrollTo({
      left: index * element.clientWidth,
      behavior: reduceMotion ? "instant" : "smooth",
    })
  }, [])

  return (
    <div className="flex h-dvh flex-col overflow-hidden">
      <GlobalSearch
        onSelectTrack={onSelectTrack}
        onSelectArtist={() => navigate(artistsPage)}
        className="border-b p-2"
      />
      <div
        ref={strip}
        className="flex min-h-0 flex-1 snap-x snap-mandatory [scrollbar-width:none] overflow-x-auto overflow-y-hidden overscroll-x-contain [&::-webkit-scrollbar]:hidden"
      >
        <div
          inert={active !== libraryPage}
          className={cn(pageClassName, "gap-3 p-3")}
        >
          <div className="flex items-center gap-2">
            <h1 className="text-xl font-semibold">Library</h1>
            {total !== undefined && (
              <span className="text-muted-foreground">{total} tracks</span>
            )}
            <BuildInfoSheet className="ml-auto" />
            <Sheet>
              <SheetTrigger
                render={
                  <Button
                    variant="outline"
                    size="icon-lg"
                    aria-label="Filters"
                  />
                }
              >
                <SlidersHorizontal />
              </SheetTrigger>
              <SheetContent side="bottom" className="h-[85dvh]">
                <SheetTitle className="sr-only">Filters</SheetTitle>
                {filters}
              </SheetContent>
            </Sheet>
          </div>
          {table}
        </div>
        <div inert={active !== filesPage} className={cn(pageClassName, "p-3")}>
          <FilesView state={filesState} className="flex-1" />
        </div>
        <div inert={active !== artistsPage} className={cn(pageClassName, "p-3")}>
          <ArtistPage className="flex-1" />
        </div>
      </div>
      <CompactPlayer player={player} />
      <MobileNav active={active} onNavigate={navigate} />
    </div>
  )
}
