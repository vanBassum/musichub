import { Badge } from "@/components/ui/badge"
import { availabilityLabels, type Availability } from "@/lib/artist"

const classNames: Record<Availability, string> = {
  available: "bg-success text-success-foreground",
  missing: "bg-warning text-warning-foreground",
  external: "bg-secondary text-secondary-foreground",
}

export function AvailabilityBadge({
  availability,
}: {
  availability: Availability
}) {
  return (
    <Badge className={classNames[availability]}>
      {availabilityLabels[availability]}
    </Badge>
  )
}
