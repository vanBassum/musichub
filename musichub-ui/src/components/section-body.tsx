import type { ReactNode } from "react"

import { Button } from "@/components/ui/button"
import type { SectionState } from "@/lib/artist"

interface SectionBodyProps<T> {
  state: SectionState<T>
  // What the section is called in messages, e.g. "popular tracks".
  noun: string
  isEmpty: (data: T) => boolean
  onRetry: () => void
  loading: ReactNode
  children: (data: T) => ReactNode
}

// One section's loading, error and empty states, so no section waits for another.
export function SectionBody<T>({
  state,
  noun,
  isEmpty,
  onRetry,
  loading,
  children,
}: SectionBodyProps<T>) {
  if (state.status === "loading") {
    return (
      <div role="status" aria-label={`Loading ${noun}`} aria-busy="true">
        {loading}
      </div>
    )
  }
  if (state.status === "error") {
    return (
      <div
        role="alert"
        className="flex flex-wrap items-center gap-3 rounded-md border border-destructive/40 p-3 text-sm"
      >
        <span className="text-destructive">
          Could not load {noun}. {state.message}
        </span>
        <Button variant="outline" size="sm" onClick={onRetry}>
          Try again
        </Button>
      </div>
    )
  }
  if (isEmpty(state.data)) {
    return (
      <p className="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
        No {noun} yet.
      </p>
    )
  }
  return <>{children(state.data)}</>
}

export function SkeletonBar({ className }: { className: string }) {
  return (
    <div className={`animate-pulse rounded-md bg-muted ${className}`} />
  )
}
