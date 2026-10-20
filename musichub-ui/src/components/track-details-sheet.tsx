import { AudioBadge } from "@/components/audio-badge"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet"
import { formatDate } from "@/lib/format"
import { useTrack } from "@/lib/use-catalog"

interface TrackDetailsSheetProps {
  // The selected track; null closes the sheet.
  trackId: number | null
  onClose: () => void
}

// Details of the selected catalog track, read from GET /api/tracks/{id}.
export function TrackDetailsSheet({
  trackId,
  onClose,
}: TrackDetailsSheetProps) {
  const track = useTrack(trackId)

  return (
    <Sheet
      open={trackId !== null}
      onOpenChange={(open) => {
        if (!open) onClose()
      }}
    >
      <SheetContent>
        <SheetHeader>
          <SheetTitle>{track.data?.title ?? "Track details"}</SheetTitle>
          <SheetDescription>
            {track.data?.version ?? "Catalog track"}
          </SheetDescription>
        </SheetHeader>
        <div className="p-4 pt-0 text-sm">
          {track.status === "loading" && (
            <p role="status" className="text-muted-foreground">
              Loading track…
            </p>
          )}
          {track.status === "error" && (
            <p role="alert" className="text-destructive">
              Could not load this track. {track.error}
            </p>
          )}
          {track.data && (
            <dl className="flex flex-col gap-3">
              <div className="flex flex-col gap-0.5">
                <dt className="text-muted-foreground">Stored audio</dt>
                <dd>
                  <AudioBadge hasAudio={track.data.hasAudio} />
                </dd>
              </div>
              <div className="flex flex-col gap-0.5">
                <dt className="text-muted-foreground">
                  {track.data.artists.length === 1 ? "Artist" : "Artists"}
                </dt>
                <dd>
                  {track.data.artists.length === 0 ? (
                    <span className="text-muted-foreground">No artist</span>
                  ) : (
                    <ul>
                      {track.data.artists.map((artist) => (
                        <li key={artist.artistId}>
                          {artist.name}
                          {artist.role && (
                            <span className="text-muted-foreground">
                              {" "}
                              ({artist.role})
                            </span>
                          )}
                        </li>
                      ))}
                    </ul>
                  )}
                </dd>
              </div>
              <div className="flex flex-col gap-0.5">
                <dt className="text-muted-foreground">Added to catalog</dt>
                <dd>{formatDate(track.data.createdAt)}</dd>
              </div>
            </dl>
          )}
        </div>
      </SheetContent>
    </Sheet>
  )
}
