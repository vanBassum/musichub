import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"

// Whether stored audio is linked to a track; tracks without audio stay in the catalog.
export function AudioBadge({ hasAudio }: { hasAudio: boolean }) {
  return (
    <Badge
      variant={hasAudio ? "default" : "outline"}
      className={cn(
        "min-w-10",
        hasAudio
          ? "bg-success text-success-foreground"
          : "text-muted-foreground"
      )}
    >
      {hasAudio ? "Audio" : "No audio"}
    </Badge>
  )
}
