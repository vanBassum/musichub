import { EllipsisVertical, ListPlus, Play } from "lucide-react"

import { AvailabilityBadge } from "@/components/availability-badge"
import { CoverArt } from "@/components/cover-art"
import { SectionBody, SkeletonBar } from "@/components/section-body"
import { Button } from "@/components/ui/button"
import type { PopularTrack, SectionState } from "@/lib/artist"

interface ArtistPopularTracksProps {
  artistName: string
  state: SectionState<PopularTrack[]>
  onRetry: () => void
}

const demoHint = "Demo only: not available yet"

export function ArtistPopularTracks({
  artistName,
  state,
  onRetry,
}: ArtistPopularTracksProps) {
  return (
    <section
      aria-labelledby="popular-tracks-heading"
      className="min-w-0 space-y-3"
    >
      <h2 id="popular-tracks-heading" className="text-lg font-semibold">
        Popular tracks
      </h2>
      <SectionBody
        state={state}
        noun="popular tracks"
        isEmpty={(tracks) => tracks.length === 0}
        onRetry={onRetry}
        loading={
          <div className="space-y-2">
            {[0, 1, 2, 3, 4].map((row) => (
              <SkeletonBar key={row} className="h-12 w-full" />
            ))}
          </div>
        }
      >
        {(tracks) => (
          <ul className="space-y-2">
            {tracks.map((track) => (
              <li
                key={track.id}
                className="flex items-center gap-3 rounded-lg border bg-card p-2"
              >
                <CoverArt
                  track={{ album: track.album, artist: artistName }}
                  className="size-10 text-xs"
                />
                <div className="min-w-0 flex-1">
                  <div className="truncate font-medium">{track.title}</div>
                  <div className="truncate text-xs text-muted-foreground">
                    {track.album} · {track.year}
                  </div>
                </div>
                <AvailabilityBadge availability={track.availability} />
                <div className="flex shrink-0 items-center gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    disabled
                    title={demoHint}
                    aria-label={`Play ${track.title} (demo)`}
                  >
                    <Play />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    disabled
                    title={demoHint}
                    aria-label={`Add ${track.title} to wishlist (demo)`}
                  >
                    <ListPlus />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    disabled
                    title={demoHint}
                    aria-label={`More actions for ${track.title} (demo)`}
                  >
                    <EllipsisVertical />
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </SectionBody>
    </section>
  )
}
