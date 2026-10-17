import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"
import { trackStatuses, type TrackStatus } from "@/data/mock-tracks"

export const trackStatusClassNames: Record<TrackStatus, string> = {
  ok: "bg-success text-success-foreground",
  unknown: "bg-destructive text-white",
  new: "bg-primary text-primary-foreground",
  "to-check": "bg-warning text-warning-foreground",
}

export function TrackStatusBadge({ status }: { status: TrackStatus }) {
  const label = trackStatuses.find((s) => s.value === status)?.label ?? status
  return (
    <Badge className={cn("min-w-10", trackStatusClassNames[status])}>
      {label}
    </Badge>
  )
}
