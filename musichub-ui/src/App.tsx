import { useState } from "react"

import { AppHeader, type AppView } from "@/components/app-header"
import { FilesView } from "@/components/files-view"
import { FilterSidebar } from "@/components/filter-sidebar"
import { MobileShell } from "@/components/mobile-shell"
import { PlayerPanel } from "@/components/player-panel"
import { TrackTable } from "@/components/track-table"
import { mockNowPlayingId, mockQueue, mockTracks } from "@/data/mock-tracks"
import { useFiles } from "@/lib/use-files"
import { useIsDesktop } from "@/lib/use-media-query"

export function App() {
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [view, setView] = useState<AppView>("library")
  const isDesktop = useIsDesktop()
  // Held here so uploads survive navigation and the desktop/mobile layout swap.
  const filesState = useFiles()

  // One tree at a time, so assistive tech never sees duplicate landmarks or ids.
  if (!isDesktop) {
    return (
      <MobileShell
        tracks={mockTracks}
        queue={mockQueue}
        nowPlayingId={mockNowPlayingId}
        selectedId={selectedId}
        onSelect={setSelectedId}
        filesState={filesState}
      />
    )
  }

  return (
    <div className="flex min-h-svh flex-col lg:h-svh">
      <AppHeader view={view} onNavigate={setView} />
      {view === "files" ? (
        <main className="flex min-h-0 flex-1 flex-col p-4">
          <FilesView state={filesState} className="flex-1" />
        </main>
      ) : (
        <main className="grid min-h-0 flex-1 gap-4 p-4 lg:grid-cols-[15rem_minmax(0,1fr)_20rem] xl:grid-cols-[17rem_minmax(0,1fr)_24rem]">
          <FilterSidebar tracks={mockTracks} />
          <TrackTable
            tracks={mockTracks}
            selectedId={selectedId}
            nowPlayingId={mockNowPlayingId}
            onSelect={setSelectedId}
          />
          <PlayerPanel queue={mockQueue} nowPlayingId={mockNowPlayingId} />
        </main>
      )}
    </div>
  )
}

export default App
