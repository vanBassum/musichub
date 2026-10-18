import { useState } from "react"

import { ArtistAlbums } from "@/components/artist-albums"
import { ArtistHeader } from "@/components/artist-header"
import { ArtistPopularTracks } from "@/components/artist-popular-tracks"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs"
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
// useDemoSection: the header, tracks and albums each have their own state, and
// none of them is hidden by another's loading, empty or error state.
export function ArtistPage({ className }: { className?: string }) {
  const [tab, setTab] = useState<Tab>("overview")
  const artist = useDemoSection<Artist | null>(mockArtist, null)
  const tracks = useDemoSection(mockPopularTracks, [])
  const albums = useDemoSection(mockAlbums, [])
  // Only seeds the cover art; the sections render whatever the header state is.
  const name =
    artist.state.status === "ready" ? (artist.state.data?.name ?? "") : ""

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
      <Tabs value={tab} onValueChange={(value) => setTab(value as Tab)}>
        <TabsList variant="line" aria-label="Artist sections">
          {tabs.map(({ id, label }) => (
            <TabsTrigger key={id} value={id}>
              {label}
            </TabsTrigger>
          ))}
          <TabsTrigger
            value="similar"
            disabled
            title="Demo only: not available yet"
          >
            Similar artists
          </TabsTrigger>
        </TabsList>
        <TabsContent value={tab} className="space-y-6">
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
        </TabsContent>
      </Tabs>
    </div>
  )
}
