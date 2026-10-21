import { useEffect, useState } from "react"

import { AppHeader } from "@/components/app-header"
import { ArtistPage } from "@/components/artist-page"
import { DownloadsView } from "@/components/downloads-view"
import { FilterSidebar } from "@/components/filter-sidebar"
import { MobileShell } from "@/components/mobile-shell"
import { PlayerPanel } from "@/components/player-panel"
import { SearchPage } from "@/components/search-page"
import { SettingsPage } from "@/components/settings-page"
import { TrackDetailsSheet } from "@/components/track-details-sheet"
import { TrackTable } from "@/components/track-table"
import type { ArtistRef } from "@/lib/artist"
import type { AudioFilter } from "@/lib/catalog-api"
import {
  activeNav,
  formatLocation,
  locationOf,
  parseSearchQuery,
  syncSearchHash,
  type AppLocation,
} from "@/lib/navigation"
import { useTracks } from "@/lib/use-catalog"
import { useDownloadQueue } from "@/lib/use-download-queue"
import { useFiles } from "@/lib/use-files"
import { useJobs } from "@/lib/use-jobs"
import { useLocation } from "@/lib/use-location"
import { useIsDesktop } from "@/lib/use-media-query"
import { useProblems } from "@/lib/use-problems"

const searchDelayMs = 300

export function App() {
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const location = useLocation()
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
  const downloadsState = useDownloadQueue()
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
    if (location.tab !== "search") return
    const timer = setTimeout(() => syncSearchHash(searchQuery), searchDelayMs)
    return () => clearTimeout(timer)
  }, [location.tab, searchQuery])

  // A navigation is a history entry, so browser back returns to where it came from.
  const navigate = (next: AppLocation) => {
    window.location.hash = formatLocation(next, searchQuery)
  }

  const openArtist = (artist: ArtistRef) => {
    setArtistRef(artist)
    navigate(locationOf("artist"))
  }

  const openExecution = (executionId: string) => {
    jobsState.select(executionId)
    navigate(locationOf("settings", "jobs"))
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
          location={location}
          onNavigate={navigate}
          total={catalog.data?.total}
          filters={filters(
            "flex-1 rounded-none border-0 bg-transparent pt-12"
          )}
          table={table(false)}
          filesState={filesState}
          jobsState={jobsState}
          downloadsState={downloadsState}
          problemsState={problemsState}
          onOpenExecution={openExecution}
          artistRef={artistRef}
          searchQuery={searchQuery}
          onSearchQueryChange={setSearchQuery}
          onSelectTrack={setSelectedId}
          onOpenArtist={openArtist}
        />
        {details}
      </>
    )
  }

  // The phone's Player page has no desktop counterpart: the player is docked.
  const tab = location.tab === "player" ? "library" : location.tab

  return (
    <div className="flex min-h-svh flex-col lg:h-svh">
      <AppHeader
        active={activeNav(tab)}
        onNavigate={(id) => navigate(locationOf(id))}
      />
      {tab === "library" ? (
        <main className="grid min-h-0 flex-1 gap-4 p-4 lg:grid-rows-[minmax(0,1fr)] lg:grid-cols-[15rem_minmax(0,1fr)_20rem] xl:grid-cols-[17rem_minmax(0,1fr)_24rem]">
          {filters()}
          {table(true)}
          <PlayerPanel />
        </main>
      ) : (
        // The player stays docked beside every view, so playback can be controlled anywhere.
        <main className="grid min-h-0 flex-1 gap-4 p-4 lg:grid-rows-[minmax(0,1fr)] lg:grid-cols-[minmax(0,1fr)_20rem] xl:grid-cols-[minmax(0,1fr)_24rem]">
          {tab === "search" ? (
            <SearchPage
              query={searchQuery}
              onQueryChange={setSearchQuery}
              onSelectTrack={setSelectedId}
              onSelectArtist={openArtist}
              autoFocus
              className="min-h-0"
            />
          ) : tab === "downloads" ? (
            <DownloadsView state={downloadsState} className="min-h-0" />
          ) : tab === "settings" ? (
            <SettingsPage
              screen={location.screen ?? "files"}
              onOpenScreen={(id) => navigate(locationOf("settings", id))}
              filesState={filesState}
              jobsState={jobsState}
              problemsState={problemsState}
              onOpenExecution={openExecution}
              className="min-h-0"
            />
          ) : (
            <ArtistPage artistRef={artistRef} className="min-h-0" />
          )}
          <PlayerPanel />
        </main>
      )}
      {details}
    </div>
  )
}

export default App
