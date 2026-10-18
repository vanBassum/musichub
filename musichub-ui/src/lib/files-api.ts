export type FileStatus = "Pending" | "Uploading" | "Stored" | "Failed" | "Expired"

// One row of GET /api/files: a persisted registration, not a track.
export interface FileRegistration {
  id: string
  originalFilename: string
  sizeBytes: number | null
  status: FileStatus
  startedAt: string
  failureReason: string | null
  statusDetail: string | null
}

export async function listFiles(signal?: AbortSignal) {
  const response = await fetch("/api/files", { signal })
  if (!response.ok) {
    throw new Error(`The file list could not be loaded (HTTP ${response.status}).`)
  }
  return (await response.json()) as FileRegistration[]
}

export interface UploadHandle {
  // Resolves only when the API confirms the file as Stored: its 201, or, after a lost
  // response, the persisted status from GET /api/files.
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

// The API compares what it receives with this length and digest, so the checksum is
// computed before the first byte is sent. The browser hashes the file in one call.
async function sha256Hex(file: File) {
  if (!globalThis.crypto?.subtle) {
    throw new UploadError(
      "This browser can't compute the file checksum (it needs an https or localhost page)."
    )
  }
  const digest = await crypto.subtle.digest("SHA-256", await file.arrayBuffer())
  return Array.from(new Uint8Array(digest), (b) =>
    b.toString(16).padStart(2, "0")
  ).join("")
}

// The request failed without an answer: the file may or may not have been stored.
class LostResponse extends UploadError {}

// Registrations are numbered in creation order, so anything above the id seen before
// the upload started was registered after it began.
async function newestFileId() {
  try {
    return Math.max(0, ...(await listFiles()).map((f) => Number(f.id)))
  } catch {
    return null
  }
}

// The persisted status is the authority when the response was lost.
async function storedSince(file: File, baselineId: number | null) {
  if (baselineId === null) return false
  try {
    return (await listFiles()).some(
      (f) =>
        Number(f.id) > baselineId &&
        f.status === "Stored" &&
        f.originalFilename === file.name &&
        f.sizeBytes === file.size
    )
  } catch {
    return false
  }
}

// POST /api/files streams the raw body. XMLHttpRequest is used because fetch can't
// report upload progress. `onPrepared` fires when the checksum is ready and sending
// starts; `onProgress` reports bytes handed to the network. Neither is proof of
// storage: only the API's 201 or the persisted `Stored` status is.
export function uploadFile(
  file: File,
  onPrepared: () => void,
  onProgress: (loaded: number, total: number) => void
): UploadHandle {
  const request = new XMLHttpRequest()
  let cancelled = false
  const result = (async () => {
    const [sha256, baselineId] = await Promise.all([
      sha256Hex(file),
      newestFileId(),
    ])
    if (cancelled) throw new UploadCancelled()
    onPrepared()
    try {
      await send(request, file, sha256, onProgress)
    } catch (error) {
      if (error instanceof LostResponse && (await storedSince(file, baselineId))) {
        return
      }
      throw error
    }
  })()
  return {
    result,
    cancel: () => {
      cancelled = true
      request.abort()
    },
  }
}

function send(
  request: XMLHttpRequest,
  file: File,
  sha256: string,
  onProgress: (loaded: number, total: number) => void
) {
  return new Promise<void>((resolve, reject) => {
    request.open(
      "POST",
      `/api/files?filename=${encodeURIComponent(file.name)}&size=${file.size}&sha256=${sha256}`
    )
    request.setRequestHeader(
      "Content-Type",
      file.type || "application/octet-stream"
    )
    request.upload.onprogress = (event) => {
      if (event.lengthComputable) onProgress(event.loaded, event.total)
    }
    request.onerror = () =>
      reject(new LostResponse("The connection to the server failed. Upload the file again."))
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
