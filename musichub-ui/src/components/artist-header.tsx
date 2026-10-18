import { CoverArt } from "@/components/cover-art"
import { SectionBody } from "@/components/section-body"
import { Badge } from "@/components/ui/badge"
import { Skeleton } from "@/components/ui/skeleton"
import type { Artist, SectionState } from "@/lib/artist"

interface ArtistHeaderProps {
  state: SectionState<Artist | null>
  onRetry: () => void
}

export function ArtistHeader({ state, onRetry }: ArtistHeaderProps) {
  return (
    <section
      aria-label="Artist"
      className="overflow-hidden rounded-xl border bg-gradient-to-br from-primary/30 via-card to-card p-4 sm:p-6"
    >
      <SectionBody
        state={state}
        noun="the artist"
        isEmpty={(artist) => artist === null}
        onRetry={onRetry}
        loading={
          <div className="flex items-center gap-4">
            <Skeleton className="size-24 rounded-full sm:size-28" />
            <div className="flex-1 space-y-2">
              <Skeleton className="h-7 w-48 max-w-full" />
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-4 w-full max-w-md" />
            </div>
          </div>
        }
      >
        {(artist) =>
          artist && (
            <div className="flex flex-col gap-4 sm:flex-row sm:items-center">
              <CoverArt
                track={{ album: artist.name, artist: artist.name }}
                className="size-24 rounded-full text-2xl sm:size-28"
              />
              <div className="min-w-0 flex-1 space-y-2">
                <h1 className="text-2xl font-semibold break-words sm:text-3xl">
                  {artist.name}
                </h1>
                <p className="text-sm text-muted-foreground">
                  Artist · {artist.genres.join(" · ")}
                </p>
                <p className="max-w-prose text-sm">{artist.description}</p>
              </div>
              <div className="flex flex-wrap gap-2 sm:flex-col sm:items-end">
                <Badge variant="outline">
                  {artist.origin === "local" ? "Local artist" : "External artist"}
                </Badge>
                <Badge
                  className={
                    artist.inCatalog
                      ? "bg-success text-success-foreground"
                      : "bg-secondary text-secondary-foreground"
                  }
                >
                  {artist.inCatalog ? "In catalog" : "Not in catalog"}
                </Badge>
              </div>
            </div>
          )
        }
      </SectionBody>
    </section>
  )
}
