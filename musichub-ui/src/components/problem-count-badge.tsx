import { Badge } from "@/components/ui/badge"

// Shown beside Settings and its Problems section, so problems stay visible without a tab of their own.
export function ProblemCountBadge({ count }: { count: number }) {
  if (count === 0) return null
  return (
    <Badge
      aria-label={`${count} ${count === 1 ? "problem" : "problems"}`}
      className="bg-destructive text-white"
    >
      {count}
    </Badge>
  )
}
