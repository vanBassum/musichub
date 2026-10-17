import { useState } from "react"

import { AppHeader } from "@/components/app-header"
import { FilterSidebar } from "@/components/filter-sidebar"
import { PlayerPanel } from "@/components/player-panel"
import { TrackTable } from "@/components/track-table"
import { mockNowPlayingId, mockQueue, mockTracks } from "@/data/mock-tracks"

export function App() {
  const [selectedId, setSelectedId] = useState<string | null>(null)

  return (
    <div className="flex min-h-svh flex-col lg:h-svh">
      <AppHeader />
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
    </div>
  )
}

export default App
