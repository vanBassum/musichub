export type FileStatus = "Pending" | "Uploading" | "Stored" | "Failed"

// One row of GET /api/files: a persisted registration, not a track.
export interface FileRegistration {
  id: string
  originalFilename: string
  sizeBytes: number | null
  status: FileStatus
  startedAt: string
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

// POST /api/files streams the raw body. XMLHttpRequest is used because fetch can't
// report upload progress. `onProgress` reports bytes handed to the network; it is
// not proof of storage, only the API's 201 is.
export function uploadFile(
  file: File,
  onProgress: (loaded: number, total: number) => void
): UploadHandle {
  const request = new XMLHttpRequest()
  const result = new Promise<void>((resolve, reject) => {
    request.open(
      "POST",
      `/api/files?filename=${encodeURIComponent(file.name)}`
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
    request.onabort = () => reject(new UploadError("Upload cancelled."))
    request.onload = () => {
      if (request.status === 201 && storedResponse(request.responseText)) {
        resolve()
        return
      }
      reject(new UploadError(failureMessage(request)))
    }
    request.send(file)
  })
  return { result, cancel: () => request.abort() }
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
