// The recurring schedule of a task. `enabled` is false when no cron is configured or `problem` says why it
// could not be registered. Times are UTC instants.
export interface JobSchedule {
  enabled: boolean
  cron: string | null
  nextRun: string | null
  lastRun: string | null
  problem: string | null
}

// One background task of GET /api/jobs. `activeExecutionId` is its run that is waiting or in progress;
// `schedule` is null for a task that cannot be scheduled.
export interface JobTask {
  id: string
  name: string
  description: string
  activeExecutionId: string | null
  schedule: JobSchedule | null
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

export type TrackAcquisitionOutcome =
  | "Enqueued"
  | "AlreadyQueued"
  | "NothingFound"
  | "ProviderUnavailable"
  | "Skipped"
  | "Failed"

// What a Search for Missing Music run did for one track.
export interface TrackAcquisitionResult {
  trackId: number
  title: string
  artists: string
  outcome: TrackAcquisitionOutcome
  detail: string
}

// What a Search for Missing Music run did; `tracks` is each track's result (the counts cover all tracks).
export interface MissingMusicSummary {
  examined: number
  enqueued: number
  alreadyQueued: number
  nothingFound: number
  providerUnavailable: number
  skipped: number
  failed: number
  tracks: TrackAcquisitionResult[]
}

export type TaskSummary = JobSummary | MissingMusicSummary

export function isMissingMusicSummary(
  summary: TaskSummary
): summary is MissingMusicSummary {
  return "enqueued" in summary
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
  summary: TaskSummary | null
  error: string | null
  // The task this is a run of (a JobTask id).
  kind: string
  // How many items the run set out to go through, when its job says so while it runs.
  total: number | null
  // Whether the run has waited in the queue or processed for too long.
  stuck: boolean
}

// Which executions the history lists; the values are the `state` query of GET /api/jobs/executions.
export const jobFilters = ["all", "failed", "stuck"] as const
export type JobFilter = (typeof jobFilters)[number]

// A page of the history; `total` counts every execution that matches the filter, not only this page.
export interface JobExecutionPage {
  items: JobExecution[]
  total: number
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
  | "Mismatch"

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
// `fileId` is set for a stored file, `downloadId` for a downloaded candidate that is not in the library.
// `pipelineVersion` is the MusicHub build whose identification methods made the attempt.
export interface FileOutcome {
  jobExecutionId: string
  fileId: number | null
  downloadId: number | null
  fileName: string
  decision: IdentificationDecision
  reason: string
  trackId: number | null
  recordedAt: string
  methods: IdentificationMethodResult[]
  pipelineVersion: string | null
}

// What an outcome is about: the stored file, or the downloaded candidate.
export const outcomeSubject = (outcome: FileOutcome) =>
  outcome.fileId !== null
    ? `file ${outcome.fileId}`
    : `download ${outcome.downloadId}`

export type DownloadStatus =
  | "Downloading"
  | "Failed"
  | "Cancelled"
  | "Rejected"
  | "Imported"

// One candidate a run downloaded, from the permanent download history. `fileId` is the library file of an import.
export interface DownloadItem {
  id: number
  trackId: number | null
  trackTitle: string | null
  provider: string
  source: string
  sourcePath: string
  sizeBytes: number
  bitrateKbps: number | null
  status: DownloadStatus
  reason: string
  fileSha256: string | null
  audioHash: string | null
  audioHashStatus: "Computed" | "Unsupported" | "Malformed" | null
  fileId: number | null
  startedAt: string
  finishedAt: string | null
}

async function problemDetail(response: Response) {
  try {
    return ((await response.json()) as { detail?: string }).detail
  } catch {
    return undefined
  }
}

export async function getJson<T>(url: string, what: string, signal?: AbortSignal) {
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

// The newest `take` executions that match `filter` (the API caps a page at 100).
export const listExecutions = (
  filter: JobFilter,
  take: number,
  signal?: AbortSignal
) =>
  getJson<JobExecutionPage>(
    `/api/jobs/executions?${new URLSearchParams({ state: filter, take: String(take) })}`,
    "The executions",
    signal
  )

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

// GET /api/jobs/executions/{id}/downloads: the candidates a Search for Missing Music run downloaded, in order.
export const getDownloads = (id: string, signal?: AbortSignal) =>
  getJson<DownloadItem[]>(
    `/api/jobs/executions/${encodeURIComponent(id)}/downloads`,
    "The downloads",
    signal
  )

// POST /api/jobs/{taskId}/start: 202 with the new execution; 409 while a run is already active (or, for a search
// for missing music, while no acquisition provider is configured).
export async function startJob(taskId: string) {
  const response = await fetch(`/api/jobs/${encodeURIComponent(taskId)}/start`, {
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
