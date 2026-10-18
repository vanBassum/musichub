import { SlidersHorizontal } from "lucide-react"
import { useCallback, useLayoutEffect, useRef, useState } from "react"

import { BuildInfoSheet } from "@/components/build-info-sheet"
import { FilesView } from "@/components/files-view"
import { FilterSidebar } from "@/components/filter-sidebar"
import { MobileNav } from "@/components/mobile-nav"
import { mobilePageIndex } from "@/components/mobile-pages"
import { NowPlaying, QueueList } from "@/components/player-panel"
import { TrackTable } from "@/components/track-table"
import { Button } from "@/components/ui/button"
import {
  Sheet,
  SheetContent,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import type { Track } from "@/data/mock-tracks"
import { cn } from "@/lib/utils"

interface MobileShellProps {
  tracks: Track[]
  queue: Track[]
  nowPlayingId: string
  selectedId: string | null
  onSelect: (id: string) => void
}

const libraryPage = mobilePageIndex("library")
const playerPage = mobilePageIndex("player")
const queuePage = mobilePageIndex("queue")
const filesPage = mobilePageIndex("files")

const pageClassName =
  "flex h-full w-full shrink-0 snap-center snap-always flex-col overflow-hidden"

// Below `lg` the three panels become full-width pages in a horizontal scroll-snap
// strip (Library, Player, Queue, Files), opening on the Player. Pages that aren't showing
// are inert, so keyboard focus can't land in them.
export function MobileShell({
  tracks,
  queue,
  nowPlayingId,
  selectedId,
  onSelect,
}: MobileShellProps) {
  const strip = useRef<HTMLDivElement>(null)
  const [active, setActive] = useState(playerPage)
  const current = queue.find((track) => track.id === nowPlayingId) ?? null

  useLayoutEffect(() => {
    const element = strip.current
    if (!element) return
    element.scrollTo({ left: playerPage * element.clientWidth })

    // A rotation changes the page width: stay on the page that is showing.
    let page = playerPage
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
            <span className="text-muted-foreground">
              {tracks.length} tracks
            </span>
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
                <FilterSidebar
                  tracks={tracks}
                  className="flex-1 rounded-none border-0 bg-transparent pt-12"
                />
              </SheetContent>
            </Sheet>
          </div>
          <TrackTable
            showHeader={false}
            tracks={tracks}
            selectedId={selectedId}
            nowPlayingId={nowPlayingId}
            onSelect={onSelect}
          />
        </div>
        <section
          aria-label="Player"
          inert={active !== playerPage}
          className={cn(pageClassName, "overflow-y-auto")}
        >
          <NowPlaying current={current} large />
        </section>
        <div inert={active !== queuePage} className={cn(pageClassName, "p-3")}>
          <div className="flex min-h-0 flex-1 flex-col rounded-xl border bg-card">
            <QueueList queue={queue} nowPlayingId={nowPlayingId} />
          </div>
        </div>
        <div inert={active !== filesPage} className={cn(pageClassName, "p-3")}>
          <FilesView className="flex-1" />
        </div>
      </div>
      <MobileNav active={active} onNavigate={navigate} />
    </div>
  )
}
