import { useEffect, useState } from "react"

import { AppHeader, type AppView } from "@/components/app-header"
import { ArtistPage } from "@/components/artist-page"
import { FilesView } from "@/components/files-view"
import { FilterSidebar } from "@/components/filter-sidebar"
import { MobileShell } from "@/components/mobile-shell"
import { PlayerPanel } from "@/components/player-panel"
import { TrackDetailsSheet } from "@/components/track-details-sheet"
import { TrackTable } from "@/components/track-table"
import { mockNowPlayingId, mockQueue } from "@/data/mock-tracks"
import type { AudioFilter } from "@/lib/catalog-api"
import { useTracks } from "@/lib/use-catalog"
import { useFiles } from "@/lib/use-files"
import { useIsDesktop } from "@/lib/use-media-query"

const searchDelayMs = 300

export function App() {
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [view, setView] = useState<AppView>("library")
  // What the box shows now, and the term sent to the API once typing pauses.
  const [searchInput, setSearchInput] = useState("")
  const [search, setSearch] = useState("")
  const [audio, setAudio] = useState<AudioFilter>("all")
  const [page, setPage] = useState(1)
  const isDesktop = useIsDesktop()
  const catalog = useTracks(search, audio, page)
  // Held here so uploads survive navigation and the desktop/mobile layout swap.
  const filesState = useFiles()

  useEffect(() => {
    const term = searchInput.trim()
    const timer = setTimeout(() => {
      setSearch(term)
      setPage(1)
    }, searchDelayMs)
    return () => clearTimeout(timer)
  }, [searchInput])

  const changeAudio = (value: AudioFilter) => {
    setAudio(value)
    setPage(1)
  }

  const filters = (className?: string) => (
    <FilterSidebar
      search={searchInput}
      onSearchChange={setSearchInput}
      audio={audio}
      onAudioChange={changeAudio}
      className={className}
    />
  )

  const table = (showHeader: boolean) => (
    <TrackTable
      showHeader={showHeader}
      status={catalog.status}
      data={catalog.data}
      error={catalog.error}
      onRetry={catalog.retry}
      onPageChange={setPage}
      filtered={search !== "" || audio !== "all"}
      selectedId={selectedId}
      onSelect={setSelectedId}
    />
  )

  const details = (
    <TrackDetailsSheet
      trackId={selectedId}
      onClose={() => setSelectedId(null)}
    />
  )

  // One tree at a time, so assistive tech never sees duplicate landmarks or ids.
  if (!isDesktop) {
    return (
      <>
        <MobileShell
          total={catalog.data?.total}
          filters={filters(
            "flex-1 rounded-none border-0 bg-transparent pt-12"
          )}
          table={table(false)}
          queue={mockQueue}
          nowPlayingId={mockNowPlayingId}
          filesState={filesState}
        />
        {details}
      </>
    )
  }

  return (
    <div className="flex min-h-svh flex-col lg:h-svh">
      <AppHeader view={view} onNavigate={setView} />
      {view === "files" ? (
        <main className="flex min-h-0 flex-1 flex-col p-4">
          <FilesView state={filesState} className="flex-1" />
        </main>
      ) : view === "artists" ? (
        <main className="flex min-h-0 flex-1 flex-col p-4">
          <ArtistPage className="flex-1" />
        </main>
      ) : (
        <main className="grid min-h-0 flex-1 gap-4 p-4 lg:grid-cols-[15rem_minmax(0,1fr)_20rem] xl:grid-cols-[17rem_minmax(0,1fr)_24rem]">
          {filters()}
          {table(true)}
          <PlayerPanel queue={mockQueue} nowPlayingId={mockNowPlayingId} />
        </main>
      )}
      {details}
    </div>
  )
}

export default App
