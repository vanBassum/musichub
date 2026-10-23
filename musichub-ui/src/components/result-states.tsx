import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"

export function LoadingRows({ label }: { label: string }) {
  return (
    <div role="status" aria-label={label} className="flex flex-col gap-1.5 p-2">
      <Skeleton className="h-4 w-3/5" />
      <Skeleton className="h-4 w-1/2" />
    </div>
  )
}

export function FailureAlert({
  message,
  onRetry,
}: {
  message: string
  onRetry: () => void
}) {
  return (
    <div role="alert" className="flex items-center gap-2 p-2 text-xs">
      <span className="flex-1">{message}</span>
      <Button size="sm" variant="outline" onClick={onRetry}>
        Retry
      </Button>
    </div>
  )
}
