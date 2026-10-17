import { Pencil, Play } from "lucide-react"

import { Button } from "@/components/ui/button"
import type { Track } from "@/data/mock-tracks"

interface TrackDetailsProps {
  track: Track | null
}

export function TrackDetails({ track }: TrackDetailsProps) {
  if (!track) {
    return (
      <aside
        aria-label="Track details"
        className="flex items-center justify-center border-t p-4 text-xs text-muted-foreground md:w-72 md:shrink-0 md:border-t-0 md:border-l"
      >
        Select a track to see its details.
      </aside>
    )
  }

  const fields: [string, string][] = [
    ["Artist", track.artist],
    ["Album", track.album],
    ["Year", String(track.year)],
    ["Genre", track.genre],
    ["Duration", track.duration],
    ["Format", track.format],
  ]

  return (
    <aside
      aria-label="Track details"
      className="flex flex-col gap-4 border-t p-4 md:w-72 md:shrink-0 md:border-t-0 md:border-l"
    >
      <h2 className="text-sm font-semibold">{track.title}</h2>
      <div className="flex gap-2">
        <Button size="sm" disabled title="Not available yet">
          <Play data-icon="inline-start" />
          Play
        </Button>
        <Button size="sm" variant="outline" disabled title="Not available yet">
          <Pencil data-icon="inline-start" />
          Edit
        </Button>
      </div>
      <dl className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-xs">
        {fields.map(([label, value]) => (
          <div key={label} className="contents">
            <dt className="text-muted-foreground">{label}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>
    </aside>
  )
}
