import { useEffect, useState } from "react"

import { AppSidebar } from "@/components/app-sidebar"
import { ArtistPage } from "@/components/artist-page"
import { FilesView } from "@/components/files-view"
import { FilterSidebar } from "@/components/filter-sidebar"
import { JobsView } from "@/components/jobs-view"
import { MobileShell } from "@/components/mobile-shell"
import { PlayerPanel } from "@/components/player-panel"
import { SearchPage } from "@/components/search-page"
import { SettingsView } from "@/components/settings-view"
import { TrackDetailsSheet } from "@/components/track-details-sheet"
import { TrackTable } from "@/components/track-table"
import { WishlistView } from "@/components/wishlist-view"
import { openArtist, type ArtistSource } from "@/lib/artist-route"
import type { AudioFilter } from "@/lib/catalog-api"
import {
  activeNav,
  formatLocation,
  locationOf,
  parseJobsFilter,
  parseSearchQuery,
  syncSearchHash,
  type AppLocation,
} from "@/lib/navigation"
import { useTracks } from "@/lib/use-catalog"
import { useFiles } from "@/lib/use-files"
import { useJobs } from "@/lib/use-jobs"
import { useLocation } from "@/lib/use-location"
import { useIsDesktop } from "@/lib/use-media-query"
import { useWishlist } from "@/lib/use-wishlist"
import type { WishlistFilter } from "@/lib/wishlist-api"

const searchDelayMs = 300

export function App() {
  const [selectedId, setSelectedId] = useState<number | null>(null)
  // Which page the selected track was opened from; its artists' pages return there.
  const [selectedSource, setSelectedSource] = useState<ArtistSource>("library")
  const location = useLocation()
  // The global search page's term, shared by the desktop page and the phone's tab.
  const [searchQuery, setSearchQuery] = useState(() =>
    parseSearchQuery(window.location.hash)
  )
  // What the box shows now, and the term sent to the API once typing pauses.
  const [searchInput, setSearchInput] = useState("")
  const [search, setSearch] = useState("")
  // The Library's wanted/missing filter lives in the URL hash, so it can be linked to.
  const { audio } = location
  const [page, setPage] = useState(1)
  const isDesktop = useIsDesktop()
  const catalog = useTracks(search, audio, page)
  // Held here so uploads survive navigation and the desktop/mobile layout swap.
  const filesState = useFiles()
  const jobsState = useJobs()
  const wishlist = useWishlist(location.wishlistFilter)

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

  // A Jobs link with a filter (#/jobs?state=failed) opens Recent runs with that filter applied.
  const { setFilter: setJobsFilter } = jobsState
  useEffect(() => {
    if (location.tab !== "jobs") return
    setJobsFilter(parseJobsFilter(window.location.hash))
  }, [location, setJobsFilter])

  // A navigation is a history entry, so browser back returns to where it came from.
  const navigate = (next: AppLocation) => {
    window.location.hash = formatLocation(next, searchQuery)
  }

  const changeAudio = (value: AudioFilter) => {
    setPage(1)
    navigate({ ...locationOf("library"), audio: value })
  }

  const changeWishlistFilter = (value: WishlistFilter) =>
    navigate({ ...locationOf("wishlist"), wishlistFilter: value })

  const filters = (className?: string) => (
    <FilterSidebar
      search={searchInput}
      onSearchChange={setSearchInput}
      audio={audio}
      onAudioChange={changeAudio}
      className={className}
    />
  )

  const selectTrack = (id: number, source: ArtistSource) => {
    setSelectedSource(source)
    setSelectedId(id)
  }

  const filtered = search !== "" || audio !== "all"

  const table = (showHeader: boolean) => (
    <TrackTable
      showHeader={showHeader}
      status={catalog.status}
      data={catalog.data}
      error={catalog.error}
      onRetry={catalog.retry}
      onPageChange={setPage}
      search={searchInput}
      onSearchChange={setSearchInput}
      filtered={filtered}
      selectedId={selectedId}
      onSelect={(id) => selectTrack(id, "library")}
      onSelectArtist={(id) =>
        openArtist({ ref: { kind: "local", id }, source: "library" })
      }
    />
  )

  const details = (
    <TrackDetailsSheet
      trackId={selectedId}
      onClose={() => setSelectedId(null)}
      onSelectArtist={(id) => {
        setSelectedId(null)
        openArtist({ ref: { kind: "local", id }, source: selectedSource })
      }}
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
          filtersActive={filtered}
          filters={filters(
            "flex-1 rounded-none border-0 bg-transparent pt-12"
          )}
          table={table(false)}
          filesState={filesState}
          jobsState={jobsState}
          wishlist={wishlist}
          onWishlistFilterChange={changeWishlistFilter}
          searchQuery={searchQuery}
          onSearchQueryChange={setSearchQuery}
        />
        {details}
      </>
    )
  }

  // The phone's Player page has no desktop counterpart: the player is docked.
  const tab = location.tab === "player" ? "library" : location.tab

  return (
    <div className="flex min-h-svh lg:h-svh">
      <AppSidebar
        active={activeNav(location)}
        onNavigate={(id) => navigate(locationOf(id))}
      />
      {tab === "library" ? (
        <main className="grid min-h-0 min-w-0 flex-1 gap-4 p-4 lg:grid-rows-[minmax(0,1fr)] lg:grid-cols-[15rem_minmax(0,1fr)_20rem] xl:grid-cols-[17rem_minmax(0,1fr)_24rem]">
          {filters()}
          {table(true)}
          <PlayerPanel />
        </main>
      ) : (
        // The player stays docked beside every view, so playback can be controlled anywhere.
        <main className="grid min-h-0 min-w-0 flex-1 gap-4 p-4 lg:grid-rows-[minmax(0,1fr)] lg:grid-cols-[minmax(0,1fr)_20rem] xl:grid-cols-[minmax(0,1fr)_24rem]">
          {tab === "search" ? (
            <SearchPage
              query={searchQuery}
              onQueryChange={setSearchQuery}
              onSelectArtist={(ref) => openArtist({ ref, source: "search" })}
              autoFocus
              className="min-h-0"
            />
          ) : tab === "wishlist" ? (
            <WishlistView
              model={wishlist}
              filter={location.wishlistFilter}
              onFilterChange={changeWishlistFilter}
              className="min-h-0"
            />
          ) : tab === "files" ? (
            <FilesView state={filesState} className="min-h-0" />
          ) : tab === "jobs" ? (
            <JobsView state={jobsState} className="min-h-0" />
          ) : tab === "settings" ? (
            <SettingsView className="min-h-0" />
          ) : (
            location.artist && (
              <ArtistPage
                route={location.artist}
                onBack={(source) => navigate(locationOf(source))}
                className="min-h-0"
              />
            )
          )}
          <PlayerPanel />
        </main>
      )}
      {details}
    </div>
  )
}

export default App
