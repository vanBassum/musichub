import { useState } from "react"

import { ArtistAlbums } from "@/components/artist-albums"
import { ArtistHeader } from "@/components/artist-header"
import { ArtistPopularTracks } from "@/components/artist-popular-tracks"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  mockAlbums,
  mockArtist,
  mockPopularTracks,
} from "@/data/mock-artist"
import type { Artist } from "@/lib/artist"
import {
  demoModes,
  useDemoSection,
  type DemoSection,
} from "@/lib/use-demo-section"
import { cn } from "@/lib/utils"

type Tab = "overview" | "albums" | "tracks"

const tabs: { id: Tab; label: string }[] = [
  { id: "overview", label: "Overview" },
  { id: "albums", label: "Albums" },
  { id: "tracks", label: "Tracks" },
]

function DemoSwitch({
  label,
  section,
}: {
  label: string
  section: DemoSection<unknown>
}) {
  return (
    <div className="flex items-center gap-1.5">
      <span className="text-xs text-muted-foreground">{label}</span>
      {demoModes.map((mode) => (
        <Button
          key={mode}
          size="xs"
          variant={section.mode === mode ? "secondary" : "ghost"}
          aria-pressed={section.mode === mode}
          onClick={() => section.setMode(mode)}
        >
          {mode}
        </Button>
      ))}
    </div>
  )
}

// One layout for local and external artists. All data is demo data, served by
// useDemoSection: each section has its own state and never blocks the others.
export function ArtistPage({ className }: { className?: string }) {
  const [tab, setTab] = useState<Tab>("overview")
  const artist = useDemoSection<Artist | null>(mockArtist, null)
  const tracks = useDemoSection(mockPopularTracks, [])
  const albums = useDemoSection(mockAlbums, [])
  const name = mockArtist.name

  return (
    <div
      className={cn(
        "min-h-0 min-w-0 space-y-4 overflow-y-auto overflow-x-hidden",
        className
      )}
    >
      <div
        role="note"
        className="flex flex-wrap items-center gap-2 rounded-lg border border-warning/50 bg-warning/10 p-3 text-sm"
      >
        <Badge className="bg-warning text-warning-foreground">Demo data</Badge>
        <span>
          This page shows made-up data, not your catalog. Play, wishlist and
          album actions are disabled.
        </span>
      </div>
      <div className="flex flex-col gap-1 rounded-lg border border-dashed p-2">
        <span className="text-xs font-medium">Demo states per section</span>
        <div className="flex flex-wrap gap-x-4 gap-y-1">
          <DemoSwitch label="Header" section={artist} />
          <DemoSwitch label="Tracks" section={tracks} />
          <DemoSwitch label="Albums" section={albums} />
        </div>
      </div>
      <ArtistHeader state={artist.state} onRetry={artist.retry} />
      <div role="tablist" aria-label="Artist sections" className="flex gap-1 border-b">
        {tabs.map(({ id, label }) => (
          <Button
            key={id}
            role="tab"
            id={`artist-tab-${id}`}
            aria-selected={tab === id}
            aria-controls="artist-tabpanel"
            variant="ghost"
            className={cn(
              "rounded-b-none",
              tab === id && "border-b-2 border-primary text-primary"
            )}
            onClick={() => setTab(id)}
          >
            {label}
          </Button>
        ))}
        <Button
          variant="ghost"
          className="rounded-b-none"
          disabled
          title="Demo only: not available yet"
        >
          Similar artists
        </Button>
      </div>
      <div
        role="tabpanel"
        id="artist-tabpanel"
        aria-labelledby={`artist-tab-${tab}`}
        className="space-y-6"
      >
        {(tab === "overview" || tab === "tracks") && (
          <ArtistPopularTracks
            artistName={name}
            state={tracks.state}
            onRetry={tracks.retry}
          />
        )}
        {(tab === "overview" || tab === "albums") && (
          <ArtistAlbums
            artistName={name}
            state={albums.state}
            onRetry={albums.retry}
          />
        )}
      </div>
    </div>
  )
}
