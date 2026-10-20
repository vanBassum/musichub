import { getOutcomes, type FileOutcome } from "@/lib/jobs-api"
import { useExecutionData } from "@/lib/use-execution-data"

export interface OutcomesState {
  outcomes: FileOutcome[] | null
  error: string | null
}

// The identification outcomes of one execution (see `useExecutionData`).
export function useOutcomes(executionId: string, version: string): OutcomesState {
  const { data, error } = useExecutionData(getOutcomes, executionId, version)
  return { outcomes: data, error }
}
