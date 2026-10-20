import { getJson } from "@/lib/jobs-api"

// Which record a problem comes from. `kind` is that source's own decision or state name: the sources record no
// severity or resolution, so none is shown.
export type ProblemSource = "identification" | "job"

export interface Problem {
  id: string
  source: ProblemSource
  kind: string
  subject: string
  reason: string
  occurredAt: string
  // The Jobs execution that holds the details, when there is one.
  jobExecutionId: string | null
  fileId: number | null
}

// `unavailable` names the sources that could not be read, so an empty list is not mistaken for "no problems".
export interface ProblemList {
  items: Problem[]
  unavailable: ProblemSource[]
}

export const listProblems = (signal?: AbortSignal) =>
  getJson<ProblemList>("/api/problems", "The problems", signal)
