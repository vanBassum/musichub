// One background task of GET /api/jobs. `activeExecutionId` is its run that is waiting or in progress.
export interface JobTask {
  id: string
  name: string
  description: string
  activeExecutionId: string | null
}

// What a catalog reconciliation run did, as the job returned it.
export interface JobSummary {
  examined: number
  linked: number
  tracksCreated: number
  unresolved: number
  skipped: number
  failed: number
}

// Hangfire's own state names.
export type JobState =
  | "Enqueued"
  | "Scheduled"
  | "Processing"
  | "Succeeded"
  | "Failed"
  | "Deleted"

export interface JobExecution {
  id: string
  state: JobState
  createdAt: string
  startedAt: string | null
  finishedAt: string | null
  summary: JobSummary | null
  error: string | null
}

export interface JobStateEntry {
  state: string
  at: string
  reason: string | null
}

export interface JobExecutionDetails {
  execution: JobExecution
  history: JobStateEntry[]
  errorType: string | null
}

// How the identification of one file in an execution ended. Only "Identified" links the file.
export type IdentificationDecision =
  | "Identified"
  | "NoMatch"
  | "InsufficientEvidence"
  | "NoMethodRan"
  | "MethodFailed"
  | "Skipped"
  | "Error"

export type IdentificationMethodStatus =
  | "Found"
  | "NothingFound"
  | "Unconfigured"
  | "Failed"
  | "NotRun"

// What one identification method did for a file; `candidates` are the matches it suggested, as text.
export interface IdentificationMethodResult {
  source: string
  status: IdentificationMethodStatus
  note: string
  candidates: string[]
}

// `jobExecutionId` is the execution that recorded the outcome; Hangfire may have dropped that execution since.
export interface FileOutcome {
  jobExecutionId: string
  fileId: number
  fileName: string
  decision: IdentificationDecision
  reason: string
  trackId: number | null
  recordedAt: string
  methods: IdentificationMethodResult[]
}

async function problemDetail(response: Response) {
  try {
    return ((await response.json()) as { detail?: string }).detail
  } catch {
    return undefined
  }
}

async function getJson<T>(url: string, what: string, signal?: AbortSignal) {
  const response = await fetch(url, { signal })
  if (!response.ok) {
    throw new Error(
      (await problemDetail(response)) ??
        `${what} could not be loaded (HTTP ${response.status}).`
    )
  }
  return (await response.json()) as T
}

export const listJobs = (signal?: AbortSignal) =>
  getJson<JobTask[]>("/api/jobs", "The task list", signal)

export const listExecutions = (signal?: AbortSignal) =>
  getJson<JobExecution[]>("/api/jobs/executions", "The executions", signal)

export const getExecution = (id: string, signal?: AbortSignal) =>
  getJson<JobExecutionDetails>(
    `/api/jobs/executions/${encodeURIComponent(id)}`,
    "The execution",
    signal
  )

// GET /api/jobs/executions/{id}/outcomes: one outcome per file the execution examined.
export const getOutcomes = (id: string, signal?: AbortSignal) =>
  getJson<FileOutcome[]>(
    `/api/jobs/executions/${encodeURIComponent(id)}/outcomes`,
    "The file outcomes",
    signal
  )

export interface OutcomePage {
  items: FileOutcome[]
  total: number
  page: number
  pageSize: number
}

// GET /api/identification-history: recorded outcomes across executions, newest first. Without a decision only the
// unresolved ones; with one, that decision.
export const getIdentificationHistory = (
  query: { decision: IdentificationDecision | null; page: number; pageSize: number },
  signal?: AbortSignal
) => {
  const params = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize),
  })
  if (query.decision) params.set("decision", query.decision)
  return getJson<OutcomePage>(
    `/api/identification-history?${params}`,
    "The identification history",
    signal
  )
}

// POST /api/jobs/catalog-reconciliation/start: 202 with the new execution; 409 while a run is already active.
export async function startCatalogReconciliation() {
  const response = await fetch("/api/jobs/catalog-reconciliation/start", {
    method: "POST",
  })
  if (!response.ok) {
    throw new Error(
      (await problemDetail(response)) ??
        `The task could not be started (HTTP ${response.status}).`
    )
  }
  return ((await response.json()) as { executionId: string }).executionId
}
