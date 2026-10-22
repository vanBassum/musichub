import {
  getJson,
  type FileOutcome,
  type IdentificationDecision,
} from "@/lib/jobs-api"

export type FileStatus =
  | "Pending"
  | "Uploading"
  | "Stored"
  | "Failed"
  | "Expired"
  | "Deleting"

// Extensions the API streams for playback (GET /api/files/{id}/audio answers 415 otherwise).
const audioExtensions = [
  ".mp3",
  ".flac",
  ".wav",
  ".ogg",
  ".oga",
  ".opus",
  ".m4a",
  ".aac",
  ".weba",
]

export function isAudioFilename(name: string) {
  const lower = name.toLowerCase()
  return audioExtensions.some((extension) => lower.endsWith(extension))
}

export const fileStatuses: FileStatus[] = [
  "Stored",
  "Pending",
  "Uploading",
  "Failed",
  "Expired",
  "Deleting",
]

// Where a Stored file stands in identification, from what is already recorded: its link, who applied it and how the
// latest attempt ended. "Uncertain" is the existing InsufficientEvidence decision (candidates that did not justify an
// identity); there is no confidence score. Unidentified is every unlinked file: the other unlinked states split it.
export type FileIdentificationState =
  | "Identified"
  | "ManuallyConfirmed"
  | "Unidentified"
  | "NotAttempted"
  | "Uncertain"
  | "NoMatch"
  | "NoMethodRan"
  | "MethodFailed"
  | "Error"
  | "Mismatch"

export const identificationStates: Record<
  FileIdentificationState,
  { label: string; hint: string }
> = {
  Identified: { label: "Identified", hint: "Linked to a track" },
  ManuallyConfirmed: {
    label: "Confirmed by you",
    hint: "Linked, and a person confirmed or corrected the track",
  },
  Unidentified: { label: "Unidentified", hint: "Not linked to a track" },
  NotAttempted: {
    label: "Not attempted",
    hint: "Not linked, and no unresolved identification attempt is recorded",
  },
  Uncertain: {
    label: "Uncertain",
    hint: "The latest attempt found candidates that did not justify an identity",
  },
  NoMatch: { label: "No match", hint: "The latest attempt found nothing" },
  NoMethodRan: {
    label: "No method could run",
    hint: "The latest attempt had no identification method that could run",
  },
  MethodFailed: {
    label: "A method failed",
    hint: "The latest attempt found nothing and a method failed, so a match may have been missed",
  },
  Error: { label: "Error", hint: "Processing the file failed in the latest attempt" },
  Mismatch: {
    label: "Another recording",
    hint: "The latest attempt identified another recording than the one wanted",
  },
}

// The filters, in the order they are offered.
export const identificationFilters = Object.keys(
  identificationStates
) as FileIdentificationState[]

// How the identification of a Stored file stands; `decision` is how the latest recorded attempt ended.
export interface FileIdentification {
  state: FileIdentificationState
  source: "Automatic" | "Manual" | null
  trackId: number | null
  trackTitle: string | null
  decision: IdentificationDecision | null
  reason: string | null
  recordedAt: string | null
}

// One row of GET /api/files: a persisted registration, not a track. `identification` is null unless it is Stored.
export interface FileRegistration {
  id: string
  originalFilename: string
  sizeBytes: number | null
  status: FileStatus
  startedAt: string
  statusDetail: string | null
  deletionError: string | null
  identification: FileIdentification | null
}

// `total` counts every registration that matches the filters, not only this page.
export interface FilesPage {
  items: FileRegistration[]
  total: number
  page: number
  pageSize: number
}

// Registrations per status and Stored files per identification state, whatever filter a list shows.
export interface FileCounts {
  total: number
  statuses: Partial<Record<FileStatus, number>>
  identification: Record<FileIdentificationState, number>
}

export interface FilesQuery {
  status: FileStatus | null
  identification: FileIdentificationState | null
  page: number
}

export const FILES_PAGE_SIZE = 50

// Everything recorded about identifying one file, newest first; nothing in it is ever deleted.
export interface AppliedIdentification {
  source: "Automatic" | "Manual"
  trackId: number | null
  trackTitle: string | null
  attemptId: number | null
  appliedAt: string
}

export interface FileIdentificationHistory {
  attempts: FileOutcome[]
  applied: AppliedIdentification[]
}

// DELETE /api/files/{id}: removes the stored object and the registration. Safe to repeat:
// 404 means it is already gone, which is what was asked for.
export async function deleteFile(id: string) {
  const response = await fetch(`/api/files/${id}`, { method: "DELETE" })
  if (response.ok || response.status === 404) return
  let detail: string | undefined
  try {
    detail = ((await response.json()) as { detail?: string }).detail
  } catch {
    // Not a problem document: fall through.
  }
  throw new Error(
    detail ?? `The file could not be deleted (HTTP ${response.status}).`
  )
}

// What POST /api/files/delete-failed did: the API picks the Failed files itself when it runs.
// `skipped` files were no longer Failed (or being uploaded) and were left alone; `failed`
// files stay in the list as Deleting and can be deleted again.
export interface FailedFilesDeletion {
  deleted: number
  skipped: number
  failed: { fileId: number; detail: string }[]
}

export async function deleteFailedFiles() {
  const response = await fetch("/api/files/delete-failed", { method: "POST" })
  if (!response.ok) {
    throw new Error(
      `The failed files could not be deleted (HTTP ${response.status}).`
    )
  }
  return (await response.json()) as FailedFilesDeletion
}

export function listFiles(
  { status, identification, page }: FilesQuery,
  signal?: AbortSignal
) {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(FILES_PAGE_SIZE),
  })
  if (status) params.set("status", status)
  if (identification) params.set("identification", identification)
  return getJson<FilesPage>(`/api/files?${params}`, "The file list", signal)
}

export const getFileCounts = (signal?: AbortSignal) =>
  getJson<FileCounts>("/api/files/counts", "The file counts", signal)

export const getFileIdentification = (id: string, signal?: AbortSignal) =>
  getJson<FileIdentificationHistory>(
    `/api/files/${encodeURIComponent(id)}/identification`,
    "The identification history",
    signal
  )

export interface UploadHandle {
  // Resolves only when the API answers 201 with status Stored.
  result: Promise<void>
  cancel: () => void
}

export class UploadError extends Error {}

// The user cancelled: not a failure, but storage was not confirmed either.
export class UploadCancelled extends UploadError {
  constructor() {
    super("Upload cancelled.")
  }
}

// POST /api/files streams the raw body. XMLHttpRequest is used because fetch can't
// report upload progress. `onProgress` reports bytes handed to the network; it is
// not proof of storage, only the API's 201 is.
export function uploadFile(
  file: File,
  onProgress: (loaded: number, total: number) => void
): UploadHandle {
  const request = new XMLHttpRequest()
  let cancelled = false
  const result = sha256Hex(file).then(
    (sha256) =>
      new Promise<void>((resolve, reject) => {
    if (cancelled) {
      reject(new UploadCancelled())
      return
    }
    // The API requires the declared length and digest and stores the file only if
    // the bytes it received match both.
    request.open(
      "POST",
      `/api/files?filename=${encodeURIComponent(file.name)}` +
        `&expectedLength=${file.size}&expectedSha256=${sha256}`
    )
    request.setRequestHeader(
      "Content-Type",
      file.type || "application/octet-stream"
    )
    request.upload.onprogress = (event) => {
      if (event.lengthComputable) onProgress(event.loaded, event.total)
    }
    request.onerror = () =>
      reject(new UploadError("The connection to the server failed. Upload the file again."))
    request.onabort = () => reject(new UploadCancelled())
    request.onload = () => {
      if (request.status === 201 && storedResponse(request.responseText)) {
        resolve()
        return
      }
      reject(new UploadError(failureMessage(request)))
    }
    request.send(file)
      })
  )
  return {
    result,
    cancel: () => {
      cancelled = true
      request.abort()
    },
  }
}

// Web Crypto has no incremental SHA-256, so the file is read into memory once.
// It is only available in a secure context (https or localhost).
async function sha256Hex(file: File) {
  if (!globalThis.crypto?.subtle) {
    throw new UploadError(
      "This page cannot compute the file's SHA-256 (it needs https). Open the app over https and upload again."
    )
  }
  const digest = await crypto.subtle.digest("SHA-256", await file.arrayBuffer())
  return Array.from(new Uint8Array(digest), (b) =>
    b.toString(16).padStart(2, "0")
  ).join("")
}

function storedResponse(text: string) {
  try {
    return (JSON.parse(text) as { status?: string }).status === "Stored"
  } catch {
    return false
  }
}

function failureMessage(request: XMLHttpRequest) {
  if (request.status === 413) {
    return "The file is larger than the server allows."
  }
  try {
    const problem = JSON.parse(request.responseText) as { detail?: string }
    if (problem.detail) return problem.detail
  } catch {
    // Not a problem document: fall through.
  }
  return `The server did not confirm storage (HTTP ${request.status}). Upload the file again.`
}
