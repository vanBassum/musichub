import { useState } from "react"
import { Play } from "lucide-react"

import { ProviderTrackRow } from "@/components/provider-track-row"
import { CoverArt } from "@/components/cover-art"
import { SectionBody } from "@/components/section-body"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Skeleton } from "@/components/ui/skeleton"
import { AddToLibraryButton } from "@/components/add-to-library-button"
import type { ArtistInfo, ArtistTracks, SectionState } from "@/lib/artist"
import { addExternalTrackToLibrary } from "@/lib/catalog-api"
import { presentTracks } from "@/lib/track-version"

interface ArtistTracksProps {
  artist: SectionState<ArtistInfo>
  state: SectionState<ArtistTracks>
  onRetry: () => void
}

const unavailableHint = "Not available yet"

// Not "Popular tracks": no connected provider supplies popularity data, so the tracks
// are listed alphabetically, not ranked. Live, remix, demo and similar versions are
// hidden until "Show all versions".
export function ArtistTracksSection({
  artist,
  state,
  onRetry,
}: ArtistTracksProps) {
  const artistName = artist.status === "ready" ? artist.data.name : ""
  const [showAll, setShowAll] = useState(false)
  return (
    <section aria-labelledby="tracks-heading" className="min-w-0 space-y-3">
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <h2 id="tracks-heading" className="text-lg font-semibold">
            Tracks
          </h2>
          <p className="text-xs text-muted-foreground">
            Alphabetical, not ranked: no connected source provides popularity
            data.
          </p>
        </div>
        <label className="flex min-h-9 items-center gap-2 text-sm">
          <Checkbox checked={showAll} onCheckedChange={setShowAll} />
          Show all versions
        </label>
      </div>
      <SectionBody
        state={state}
        noun="tracks"
        isEmpty={(tracks) => tracks.items.length === 0 && !tracks.warning}
        onRetry={onRetry}
        loading={
          <div className="space-y-2">
            {[0, 1, 2, 3, 4].map((row) => (
              <Skeleton key={row} className="h-12 w-full" />
            ))}
          </div>
        }
      >
        {(tracks) => (
          <div className="space-y-2">
            {tracks.warning && (
              <p role="status" className="text-sm text-muted-foreground">
                {tracks.warning}
              </p>
            )}
            <ul className="divide-y rounded-lg border bg-card">
              {presentTracks(tracks.items, showAll).map((track) => (
                <ProviderTrackRow
                  key={track.key}
                  title={track.title}
                  subtitle={track.detail || undefined}
                  availability={track.availability}
                  source={track.provenance ?? { label: "My Music" }}
                  leading={
                    <CoverArt
                      track={{ album: track.title, artist: artistName }}
                      className="size-9 text-xs"
                    />
                  }
                  actions={
                    <>
                      <Button
                        variant="ghost"
                        size="icon"
                        className="max-sm:size-9"
                        disabled
                        title={unavailableHint}
                        aria-label={`Play ${track.title}: ${unavailableHint}`}
                      >
                        <Play />
                      </Button>
                      <AddToLibraryButton
                        onAdd={
                          track.addable
                            ? () => addExternalTrackToLibrary(track.addable!)
                            : undefined
                        }
                      />
                    </>
                  }
                />
              ))}
            </ul>
          </div>
        )}
      </SectionBody>
    </section>
  )
}
