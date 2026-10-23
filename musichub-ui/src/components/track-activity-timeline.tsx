import { Skeleton } from "@/components/ui/skeleton"
import { useExecutionData } from "@/lib/use-execution-data"
import { getTrackActivity, type TrackActivityEvent } from "@/lib/wishlist-api"
import { cn } from "@/lib/utils"

// What went wrong or came to nothing stands out; the rest reads as plain progress.
const toneOf = (kind: string) =>
  /Failed$|Rejected$/.test(kind)
    ? "text-destructive"
    : kind === "NotFound" || kind.endsWith("Interrupted")
      ? "text-warning"
      : ""

// The search counts are part of the event's sentence already; the attempt number is not.
function Attempt({ event }: { event: TrackActivityEvent }) {
  return event.attemptNumber === null ? null : (
    <span className="text-muted-foreground">(attempt {event.attemptNumber})</span>
  )
}

// The persisted, chronological acquisition events of one track, exactly as recorded: a track that was never
// searched has none, and nothing is added to fill the gap. `version` changes when the track's activity does,
// so an open timeline keeps up.
export function TrackActivityTimeline({
  trackId,
  version,
}: {
  trackId: number
  version: string
}) {
  const { data, error } = useExecutionData(
    getTrackActivity,
    String(trackId),
    version
  )

  if (error)
    return (
      <p role="alert" className="text-sm text-destructive">
        {error}
      </p>
    )
  if (data === null) return <Skeleton className="h-16 w-full" />
  if (data.items.length === 0)
    return (
      <p className="text-sm text-muted-foreground">
        Nothing has been recorded for this track yet.
      </p>
    )

  return (
    <ol aria-label="Activity" className="flex flex-col gap-2 text-sm">
      {data.items.map((event) => (
        <li key={event.id} className="flex gap-3">
          <time
            dateTime={event.occurredAt}
            className="w-40 shrink-0 text-xs text-muted-foreground"
          >
            {new Date(event.occurredAt).toLocaleString()}
          </time>
          <div className="min-w-0">
            <p className={cn("break-words", toneOf(event.kind))}>
              {event.summary} <Attempt event={event} />
            </p>
            {event.detail !== null && (
              <p className="break-words text-xs whitespace-pre-wrap text-muted-foreground">
                {event.detail}
              </p>
            )}
          </div>
        </li>
      ))}
    </ol>
  )
}
