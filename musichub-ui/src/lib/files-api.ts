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

// One row of GET /api/files: a persisted registration, not a track.
export interface FileRegistration {
  id: string
  originalFilename: string
  sizeBytes: number | null
  status: FileStatus
  startedAt: string
  statusDetail: string | null
  deletionError: string | null
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

export async function listFiles(signal?: AbortSignal) {
  const response = await fetch("/api/files", { signal })
  if (!response.ok) {
    throw new Error(`The file list could not be loaded (HTTP ${response.status}).`)
  }
  return (await response.json()) as FileRegistration[]
}

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
