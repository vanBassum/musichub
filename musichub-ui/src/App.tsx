import { useEffect, useState } from "react"

import { AppHeader, type AppView } from "@/components/app-header"
import { ArtistPage } from "@/components/artist-page"
import { FilesView } from "@/components/files-view"
import { FilterSidebar } from "@/components/filter-sidebar"
import { JobsView } from "@/components/jobs-view"
import { MobileShell } from "@/components/mobile-shell"
import {
  isSearchHash,
  parseSearchQuery,
  syncSearchHash,
} from "@/components/mobile-pages"
import { PlayerPanel } from "@/components/player-panel"
import { ProblemsView } from "@/components/problems-view"
import { SearchPage } from "@/components/search-page"
import { TrackDetailsSheet } from "@/components/track-details-sheet"
import { TrackTable } from "@/components/track-table"
import { mockNowPlayingId, mockQueue } from "@/data/mock-tracks"
import type { ArtistRef } from "@/lib/artist"
import type { AudioFilter } from "@/lib/catalog-api"
import { useTracks } from "@/lib/use-catalog"
import { useFiles } from "@/lib/use-files"
import { useJobs } from "@/lib/use-jobs"
import { useIsDesktop } from "@/lib/use-media-query"
import { useProblems } from "@/lib/use-problems"

const searchDelayMs = 300

export function App() {
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const [view, setView] = useState<AppView>(
    isSearchHash(window.location.hash) ? "search" : "library"
  )
  // The global search page's term, shared by the desktop page and the phone's tab.
  const [searchQuery, setSearchQuery] = useState(() =>
    parseSearchQuery(window.location.hash)
  )
  // The artist page shows this artist, found by a local or an external search.
  const [artistRef, setArtistRef] = useState<ArtistRef | null>(null)
  // What the box shows now, and the term sent to the API once typing pauses.
  const [searchInput, setSearchInput] = useState("")
  const [search, setSearch] = useState("")
  const [audio, setAudio] = useState<AudioFilter>("all")
  const [page, setPage] = useState(1)
  const isDesktop = useIsDesktop()
  const catalog = useTracks(search, audio, page)
  // Held here so uploads survive navigation and the desktop/mobile layout swap.
  const filesState = useFiles()
  const jobsState = useJobs()
  const problemsState = useProblems()

  useEffect(() => {
    const term = searchInput.trim()
    const timer = setTimeout(() => {
      setSearch(term)
      setPage(1)
    }, searchDelayMs)
    return () => clearTimeout(timer)
  }, [searchInput])

  // The URL hash carries the search (#/search?q=...), so it can be shared and
  // reloaded. Updated once typing pauses; Safari limits how often it may change.
  useEffect(() => {
    const timer = setTimeout(
      () => syncSearchHash(searchQuery, isDesktop, view === "search"),
      searchDelayMs
    )
    return () => clearTimeout(timer)
  }, [isDesktop, view, searchQuery])

  const openArtist =(artist: ArtistRef) => {
    setArtistRef(artist)
    setView("artists")
  }

  const openExecution = (executionId: string) => {
    jobsState.select(executionId)
    setView("jobs")
  }

  const changeAudio =(value: AudioFilter) => {
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
          jobsState={jobsState}
          problemsState={problemsState}
          artistRef={artistRef}
          searchQuery={searchQuery}
          onSearchQueryChange={setSearchQuery}
          onSelectTrack={setSelectedId}
          onSelectArtist={setArtistRef}
        />
        {details}
      </>
    )
  }

  return (
    <div className="flex min-h-svh flex-col lg:h-svh">
      <AppHeader view={view} onNavigate={setView} />
      {view === "library" ? (
        <main className="grid min-h-0 flex-1 gap-4 p-4 lg:grid-cols-[15rem_minmax(0,1fr)_20rem] xl:grid-cols-[17rem_minmax(0,1fr)_24rem]">
          {filters()}
          {table(true)}
          <PlayerPanel queue={mockQueue} nowPlayingId={mockNowPlayingId} />
        </main>
      ) : (
        // The player stays docked beside every view, so playback can be controlled anywhere.
        <main className="grid min-h-0 flex-1 gap-4 p-4 lg:grid-cols-[minmax(0,1fr)_20rem] xl:grid-cols-[minmax(0,1fr)_24rem]">
          {view === "search" ? (
            <SearchPage
              query={searchQuery}
              onQueryChange={setSearchQuery}
              onSelectTrack={setSelectedId}
              onSelectArtist={openArtist}
              autoFocus
              className="min-h-0"
            />
          ) : view === "files" ? (
            <FilesView state={filesState} className="min-h-0" />
          ) : view === "jobs" ? (
            <JobsView state={jobsState} className="min-h-0" />
          ) : view === "problems" ? (
            <ProblemsView
              state={problemsState}
              onOpenExecution={openExecution}
              className="min-h-0"
            />
          ) : (
            <ArtistPage artistRef={artistRef} className="min-h-0" />
          )}
          <PlayerPanel queue={mockQueue} nowPlayingId={mockNowPlayingId} />
        </main>
      )}
      {details}
    </div>
  )
}

export default App
