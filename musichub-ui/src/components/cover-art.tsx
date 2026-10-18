import { cn } from "@/lib/utils"

interface CoverArtProps {
  track: { album: string; artist: string }
  className?: string
}

// Mock data has no artwork: a gradient derived from the album name stands in for it.
function albumHue(album: string) {
  let hash = 0
  for (const char of album) {
    hash = (hash * 31 + char.charCodeAt(0)) % 360
  }
  return hash
}

export function CoverArt({ track, className }: CoverArtProps) {
  const hue = albumHue(track.album)
  const initials = track.artist
    .split(/\s+/)
    .filter((word) => word.length > 0 && word.toLowerCase() !== "the")
    .slice(0, 2)
    .map((word) => word[0].toUpperCase())
    .join("")

  return (
    <div
      role="img"
      aria-label={`${track.album} cover`}
      className={cn(
        "flex aspect-square shrink-0 items-center justify-center overflow-hidden rounded-md font-semibold text-white/90 shadow-sm select-none",
        className
      )}
      style={{
        backgroundImage: `linear-gradient(135deg, oklch(0.62 0.16 ${hue}), oklch(0.32 0.1 ${(hue + 50) % 360}))`,
      }}
    >
      {initials}
    </div>
  )
}
