import { ChevronLeft } from "lucide-react"
import { useState } from "react"

import { ArtistAlbums } from "@/components/artist-albums"
import { ArtistHeader } from "@/components/artist-header"
import { ArtistTracksSection } from "@/components/artist-tracks"
import { Button } from "@/components/ui/button"
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs"
import type { ArtistRef } from "@/lib/artist"
import {
  artistBackLabel,
  formatArtistHash,
  type ArtistRoute,
  type ArtistSource,
} from "@/lib/artist-route"
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
// other. Back returns to where the artist was opened from. On desktop the overview
// puts the tracks beside the albums; on a phone the sections stack in one column.
export function ArtistPage({
  route,
  onBack,
  className,
}: {
  route: ArtistRoute
  onBack: (source: ArtistSource) => void
  className?: string
}) {
  // Another artist starts on the overview again.
  return (
    <ArtistView
      key={formatArtistHash(route)}
      artistRef={route.ref}
      backLabel={artistBackLabel[route.source]}
      onBack={() => onBack(route.source)}
      className={className}
    />
  )
}

function ArtistView({
  artistRef,
  backLabel,
  onBack,
  className,
}: {
  artistRef: ArtistRef
  backLabel: string
  onBack: () => void
  className?: string
}) {
  const [tab, setTab] = useState<Tab>("overview")
  const { info, tracks } = useArtist(artistRef)

  return (
    <div
      className={cn(
        "min-h-0 min-w-0 space-y-4 overflow-y-auto overflow-x-hidden",
        className
      )}
    >
      <Button variant="ghost" onClick={onBack} className="-mb-2">
        <ChevronLeft data-icon="inline-start" />
        {backLabel}
      </Button>
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
        <TabsContent
          value={tab}
          className={cn(
            "space-y-6",
            tab === "overview" &&
              "lg:grid lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)] lg:items-start lg:gap-6 lg:space-y-0"
          )}
        >
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
