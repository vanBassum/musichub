import { EllipsisVertical, ExternalLink, ListPlus, Play } from "lucide-react"

import { AvailabilityBadge } from "@/components/availability-badge"
import { CoverArt } from "@/components/cover-art"
import { SectionBody } from "@/components/section-body"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import type { ArtistInfo, ArtistTracks, SectionState } from "@/lib/artist"

interface ArtistTracksProps {
  artist: SectionState<ArtistInfo>
  state: SectionState<ArtistTracks>
  onRetry: () => void
}

const unavailableHint = "Not available yet"

// Not "Popular tracks": no connected provider supplies popularity data, so the tracks
// are listed as the sources give them, not ranked.
export function ArtistTracksSection({
  artist,
  state,
  onRetry,
}: ArtistTracksProps) {
  const artistName = artist.status === "ready" ? artist.data.name : ""
  return (
    <section aria-labelledby="tracks-heading" className="min-w-0 space-y-3">
      <div>
        <h2 id="tracks-heading" className="text-lg font-semibold">
          Tracks
        </h2>
        <p className="text-xs text-muted-foreground">
          Not ranked: no connected source provides popularity data.
        </p>
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
            <ul className="space-y-2">
              {tracks.items.map((track) => (
                <li
                  key={track.key}
                  className="flex items-center gap-3 rounded-lg border bg-card p-2"
                >
                  <CoverArt
                    track={{ album: track.title, artist: artistName }}
                    className="size-10 text-xs"
                  />
                  <div className="min-w-0 flex-1">
                    <div className="truncate font-medium">{track.title}</div>
                    {track.detail && (
                      <div className="truncate text-xs text-muted-foreground">
                        {track.detail}
                      </div>
                    )}
                  </div>
                  {track.provenance && (
                    <a
                      href={track.provenance.url}
                      target="_blank"
                      rel="noreferrer"
                      aria-label={`Open ${track.title} on ${track.provenance.label}`}
                      className="hidden items-center gap-1 text-xs text-muted-foreground hover:text-foreground sm:flex"
                    >
                      {track.provenance.label}
                      <ExternalLink className="size-3" aria-hidden="true" />
                    </a>
                  )}
                  <AvailabilityBadge availability={track.availability} />
                  <div className="flex shrink-0 items-center gap-1">
                    <Button
                      variant="ghost"
                      size="icon"
                      disabled
                      title={unavailableHint}
                      aria-label={`Play ${track.title}`}
                    >
                      <Play />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      disabled
                      title={unavailableHint}
                      aria-label={`Add ${track.title} to wishlist`}
                    >
                      <ListPlus />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      disabled
                      title={unavailableHint}
                      aria-label={`More actions for ${track.title}`}
                    >
                      <EllipsisVertical />
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          </div>
        )}
      </SectionBody>
    </section>
  )
}
