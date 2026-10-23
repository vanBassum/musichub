import { useCallback } from "react"

import { getTrackActivity, type TrackActivity } from "@/lib/jobs-api"
import { useExecutionData } from "@/lib/use-execution-data"

export interface TrackActivityState {
  activity: TrackActivity[] | null
  error: string | null
}

// The recorded acquisition events that this execution wrote for the given tracks (see `useExecutionData`). Events
// another run or a user action wrote for the same tracks are left out.
export function useTrackActivity(
  executionId: string,
  trackIds: number[],
  version: string
): TrackActivityState {
  const key = trackIds.join(",")
  // Built from the key, so the fetcher stays the same while the ids do.
  const fetcher = useCallback(
    async (id: string, signal: AbortSignal) => {
      if (key === "") return []
      const events = await getTrackActivity(key.split(",").map(Number), signal)
      return events.filter((event) => event.jobExecutionId === id)
    },
    [key]
  )
  const { data, error } = useExecutionData(fetcher, executionId, version)
  return { activity: data, error }
}
