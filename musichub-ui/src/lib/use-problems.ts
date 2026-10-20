import { listProblems, type ProblemList } from "@/lib/problems-api"
import { useLoader } from "@/lib/use-loader"

export interface ProblemsState {
  problems: ProblemList | null
  error: string | null
  refreshing: boolean
  refresh: () => Promise<void>
}

// Loads the problem overview once and again on demand.
export function useProblems(): ProblemsState {
  const { data, error, refreshing, refresh } = useLoader(listProblems)
  return { problems: data, error, refreshing, refresh }
}
