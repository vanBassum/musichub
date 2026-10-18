import { AvailabilityBadge } from "@/components/availability-badge"
import { CoverArt } from "@/components/cover-art"
import { SectionBody } from "@/components/section-body"
import { Skeleton } from "@/components/ui/skeleton"
import type { ArtistAlbum, SectionState } from "@/lib/artist"

interface ArtistAlbumsProps {
  artistName: string
  state: SectionState<ArtistAlbum[]>
  onRetry: () => void
}

export function ArtistAlbums({
  artistName,
  state,
  onRetry,
}: ArtistAlbumsProps) {
  return (
    <section aria-labelledby="albums-heading" className="min-w-0 space-y-3">
      <h2 id="albums-heading" className="text-lg font-semibold">
        Albums
      </h2>
      <SectionBody
        state={state}
        noun="albums"
        isEmpty={(albums) => albums.length === 0}
        onRetry={onRetry}
        loading={
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
            {[0, 1, 2, 3].map((card) => (
              <Skeleton key={card} className="h-44 w-full" />
            ))}
          </div>
        }
      >
        {(albums) => (
          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
            {albums.map((album) => (
              <li
                key={album.id}
                className="min-w-0 space-y-2 rounded-lg border bg-card p-2"
              >
                <CoverArt
                  track={{ album: album.title, artist: artistName }}
                  className="w-full text-xl"
                />
                <div className="min-w-0">
                  <div className="truncate font-medium">{album.title}</div>
                  <div className="text-xs text-muted-foreground">
                    {album.year}
                  </div>
                </div>
                <AvailabilityBadge availability={album.availability} />
              </li>
            ))}
          </ul>
        )}
      </SectionBody>
    </section>
  )
}
