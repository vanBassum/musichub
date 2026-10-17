import { cn } from "@/lib/utils"
import type { Track } from "@/data/mock-tracks"

interface TrackTableProps {
  tracks: Track[]
  selectedId: string | null
  onSelect: (id: string) => void
}

export function TrackTable({ tracks, selectedId, onSelect }: TrackTableProps) {
  return (
    <div className="min-h-0 min-w-0 flex-1 overflow-auto">
      <table className="w-full min-w-[40rem] text-left text-xs">
        <thead className="sticky top-0 bg-background text-muted-foreground">
          <tr className="border-b">
            <th className="px-4 py-2 font-medium">Title</th>
            <th className="px-4 py-2 font-medium">Artist</th>
            <th className="px-4 py-2 font-medium">Album</th>
            <th className="px-4 py-2 font-medium">Year</th>
            <th className="px-4 py-2 font-medium">Duration</th>
            <th className="px-4 py-2 font-medium">Format</th>
          </tr>
        </thead>
        <tbody>
          {tracks.map((track) => {
            const selected = track.id === selectedId
            return (
              <tr
                key={track.id}
                tabIndex={0}
                aria-selected={selected}
                onClick={() => onSelect(track.id)}
                onKeyDown={(event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault()
                    onSelect(track.id)
                  }
                }}
                className={cn(
                  "cursor-pointer border-b outline-none hover:bg-muted/50 focus-visible:ring-2 focus-visible:ring-ring/30 focus-visible:ring-inset",
                  selected && "bg-muted"
                )}
              >
                <td className="px-4 py-2 font-medium">{track.title}</td>
                <td className="px-4 py-2">{track.artist}</td>
                <td className="px-4 py-2">{track.album}</td>
                <td className="px-4 py-2">{track.year}</td>
                <td className="px-4 py-2">{track.duration}</td>
                <td className="px-4 py-2">{track.format}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
