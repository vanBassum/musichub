import { ChevronLeft, ChevronRight, SlidersHorizontal } from "lucide-react"
import { useMemo, useSyncExternalStore, type ReactNode } from "react"

import { ArtistPage } from "@/components/artist-page"
import { BuildInfoSheet } from "@/components/build-info-sheet"
import { CompactPlayer } from "@/components/compact-player"
import { DownloadsView } from "@/components/downloads-view"
import { FilesView } from "@/components/files-view"
import { JobsView } from "@/components/jobs-view"
import { MobileNav } from "@/components/mobile-nav"
import {
  formatLocation,
  moreScreens,
  openSearchHash,
  parseLocation,
  type MobileLocation,
  type MobileTabId,
  type MoreScreenId,
  type PlayerView,
} from "@/components/mobile-pages"
import { NowPlaying, QueueList } from "@/components/player-panel"
import { ProblemsView } from "@/components/problems-view"
import { SearchPage } from "@/components/search-page"
import { Button } from "@/components/ui/button"
import {
  Sheet,
  SheetContent,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet"
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs"
import type { ArtistRef } from "@/lib/artist"
import type { DownloadQueueState } from "@/lib/use-download-queue"
import type { FilesState } from "@/lib/use-files"
import type { JobsState } from "@/lib/use-jobs"
import type { ProblemsState } from "@/lib/use-problems"
import { cn } from "@/lib/utils"

interface MobileShellProps {
  // The catalog's track count, once known.
  total: number | undefined
  filters: ReactNode
  table: ReactNode
  filesState: FilesState
  jobsState: JobsState
  downloadsState: DownloadQueueState
  problemsState: ProblemsState
  artistRef: ArtistRef | null
  searchQuery: string
  onSearchQueryChange: (query: string) => void
  onSelectTrack: (trackId: number) => void
  onSelectArtist: (artist: ArtistRef) => void
}

const pageClassName = "flex min-h-0 flex-1 flex-col overflow-hidden"

function subscribe(onChange: () => void) {
  window.addEventListener("hashchange", onChange)
  return () => window.removeEventListener("hashchange", onChange)
}

const getHash = () => window.location.hash

function go(location: MobileLocation) {
  window.location.hash = formatLocation(location)
}

// A page that isn't showing stays mounted (so uploads, jobs and search keep their
// state) but is hidden and inert, so keyboard focus can't land in it.
function Page({
  show,
  label,
  className,
  children,
}: {
  show: boolean
  label: string
  className?: string
  children: ReactNode
}) {
  return (
    <section
      aria-label={label}
      hidden={!show}
      inert={!show}
      className={cn(pageClassName, className)}
    >
      {children}
    </section>
  )
}

// Below `lg` the phone has three destinations: Search, Player (Now Playing and
// Queue) and More (Library, Files, Jobs, Artists). The location is the URL hash.
// Outside the Player page a compact player sits above the navigation.
export function MobileShell({
  total,
  filters,
  table,
  filesState,
  jobsState,
  downloadsState,
  problemsState,
  artistRef,
  searchQuery,
  onSearchQueryChange,
  onSelectTrack,
  onSelectArtist,
}: MobileShellProps) {
  const hash = useSyncExternalStore(subscribe, getHash)
  const location = useMemo(() => parseLocation(hash), [hash])
  const { tab, screen, playerView } = location

  const navigate = (id: MobileTabId) => {
    if (id === "search") openSearchHash(searchQuery)
    else go({ tab: id, screen: null, playerView: "now-playing" })
  }
  const openScreen = (id: MoreScreenId) =>
    go({ tab: "more", screen: id, playerView })
  const openExecution = (executionId: string) => {
    jobsState.select(executionId)
    openScreen("jobs")
  }
  const showMore =(id: MoreScreenId) => tab === "more" && screen === id

  return (
    <div className="flex h-dvh flex-col overflow-hidden">
      <Page show={tab === "search"} label="Search" className="gap-2 p-2">
        <SearchPage
          query={searchQuery}
          onQueryChange={onSearchQueryChange}
          onSelectTrack={onSelectTrack}
          onSelectArtist={(artist) => {
            onSelectArtist(artist)
            openScreen("artists")
          }}
          className="flex-1"
        />
      </Page>
      <Page show={tab === "player"} label="Player">
        <Tabs
          value={playerView}
          onValueChange={(value) =>
            go({ tab: "player", screen: null, playerView: value as PlayerView })
          }
          className="p-2 pb-0"
        >
          <TabsList className="w-full">
            <TabsTrigger value="now-playing">Now Playing</TabsTrigger>
            <TabsTrigger value="queue">Queue</TabsTrigger>
          </TabsList>
        </Tabs>
        <div
          hidden={playerView !== "now-playing"}
          className="min-h-0 flex-1 overflow-y-auto"
        >
          <NowPlaying large />
        </div>
        <div hidden={playerView !== "queue"} className="min-h-0 flex-1 p-3">
          <div className="flex h-full min-h-0 flex-col rounded-xl border bg-card">
            <QueueList />
          </div>
        </div>
      </Page>
      <Page
        show={tab === "more" && screen === null}
        label="More"
        className="gap-3 p-3"
      >
        <div className="flex items-center gap-2">
          <h1 className="text-xl font-semibold">More</h1>
          <BuildInfoSheet className="ml-auto" />
        </div>
        <ul className="flex flex-col gap-1">
          {moreScreens.map(({ id, label, icon: Icon }) => (
            <li key={id}>
              <Button
                variant="ghost"
                className="h-12 w-full justify-start gap-3 text-base"
                onClick={() => openScreen(id)}
              >
                <Icon />
                <span className="flex-1 text-left">{label}</span>
                <ChevronRight className="text-muted-foreground" />
              </Button>
            </li>
          ))}
        </ul>
      </Page>
      {moreScreens.map(({ id, label }) => (
        <Page key={id} show={showMore(id)} label={label} className="gap-3 p-3">
          <div className="flex items-center gap-2">
            <Button
              variant="ghost"
              size="icon-lg"
              aria-label="Back to More"
              onClick={() => navigate("more")}
            >
              <ChevronLeft />
            </Button>
            <h1 className="text-xl font-semibold">{label}</h1>
            {id === "library" && total !== undefined && (
              <span className="text-muted-foreground">{total} tracks</span>
            )}
            {id === "library" && (
              <Sheet>
                <SheetTrigger
                  render={
                    <Button
                      variant="outline"
                      size="icon-lg"
                      aria-label="Filters"
                      className="ml-auto"
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
            )}
          </div>
          {id === "library" && table}
          {id === "files" && (
            <FilesView state={filesState} className="flex-1" />
          )}
          {id === "jobs" && <JobsView state={jobsState} className="flex-1" />}
          {id === "downloads" && (
            <DownloadsView state={downloadsState} className="flex-1" />
          )}
          {id === "problems" && (
            <ProblemsView
              state={problemsState}
              onOpenExecution={openExecution}
              className="flex-1"
            />
          )}
          {id === "artists" && <ArtistPage artistRef={artistRef} className="flex-1" />}
        </Page>
      ))}
      {tab !== "player" && (
        <CompactPlayer />
      )}
      <MobileNav active={tab} onNavigate={navigate} />
    </div>
  )
}
