import { useState } from "react"

import { AppHeader } from "@/components/app-header"
import { FilterSidebar } from "@/components/filter-sidebar"
import { TrackDetails } from "@/components/track-details"
import { TrackTable } from "@/components/track-table"
import { mockTracks } from "@/data/mock-tracks"

export function App() {
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const selected = mockTracks.find((track) => track.id === selectedId) ?? null

  return (
    <div className="flex min-h-svh flex-col md:h-svh">
      <AppHeader />
      <main className="flex min-h-0 flex-1 flex-col md:flex-row">
        <FilterSidebar />
        <TrackTable
          tracks={mockTracks}
          selectedId={selectedId}
          onSelect={setSelectedId}
        />
        <TrackDetails track={selected} />
      </main>
    </div>
  )
}

export default App
