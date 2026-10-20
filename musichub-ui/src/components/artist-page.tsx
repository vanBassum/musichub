import { useState } from "react"

import { ArtistAlbums } from "@/components/artist-albums"
import { ArtistHeader } from "@/components/artist-header"
import { ArtistTracksSection } from "@/components/artist-tracks"
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs"
import type { ArtistRef } from "@/lib/artist"
import { useArtist } from "@/lib/use-artist"
import { cn } from "@/lib/utils"

type Tab = "overview" | "albums" | "tracks"

const tabs: { id: Tab; label: string }[] = [
  { id: "overview", label: "Overview" },
  { id: "albums", label: "Albums" },
  { id: "tracks", label: "Tracks" },
]

// One layout for local and external artists. The artist info and the tracks each load
// on their own, with their own loading, empty and error state, and neither hides the
// other.
export function ArtistPage({
  artistRef,
  className,
}: {
  artistRef: ArtistRef | null
  className?: string
}) {
  const [tab, setTab] = useState<Tab>("overview")
  const { info, tracks } = useArtist(artistRef)

  if (artistRef === null) {
    return (
      <div className={cn("min-h-0 min-w-0 p-2", className)}>
        <p className="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
          Search for an artist and open it to see its page.
        </p>
      </div>
    )
  }

  return (
    <div
      className={cn(
        "min-h-0 min-w-0 space-y-4 overflow-y-auto overflow-x-hidden",
        className
      )}
    >
      <ArtistHeader state={info.state} onRetry={info.retry} />
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
            title="Not available yet"
          >
            Similar artists
          </TabsTrigger>
        </TabsList>
        <TabsContent value={tab} className="space-y-6">
          {(tab === "overview" || tab === "tracks") && (
            <ArtistTracksSection
              artist={info.state}
              state={tracks.state}
              onRetry={tracks.retry}
            />
          )}
          {(tab === "overview" || tab === "albums") && <ArtistAlbums />}
        </TabsContent>
      </Tabs>
    </div>
  )
}
