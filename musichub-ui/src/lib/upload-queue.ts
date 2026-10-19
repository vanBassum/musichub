export const MIN_CONCURRENCY = 1
export const MAX_CONCURRENCY = 10
export const DEFAULT_CONCURRENCY = 5

export function clampConcurrency(value: number) {
  if (!Number.isFinite(value)) return DEFAULT_CONCURRENCY
  return Math.min(MAX_CONCURRENCY, Math.max(MIN_CONCURRENCY, Math.trunc(value)))
}

// What the queue needs from a running upload (`UploadHandle` in files-api fits).
export interface RunningUpload {
  result: Promise<void>
  cancel: () => void
}

export interface UploadQueueOptions<K> {
  limit?: number
  // Begins the upload for a key. Called at most `limit` times concurrently.
  start: (key: K) => RunningUpload
  onStart?: (key: K) => void
  // The upload ended: `error` is undefined only when the server confirmed storage.
  onSettled: (key: K, error?: Error) => void
  // The key was cancelled before it started; it never reached `start`.
  onDequeued: (key: K) => void
}

export interface UploadQueue<K> {
  enqueue: (key: K) => void
  // Cancels one key: removes it from the queue, or aborts it when it is running.
  cancel: (key: K) => void
  // Empties the queue and aborts every running upload. Settled uploads are untouched.
  cancelAll: () => void
  // Applies to uploads started afterwards; running ones are never interrupted.
  setLimit: (limit: number) => void
  readonly active: number
  readonly queued: number
}

// Bounded FIFO scheduler. A slot is held from `start` until the upload's promise
// settles, whichever way it ends, and is then handed to the next queued key.
export function createUploadQueue<K>(
  options: UploadQueueOptions<K>
): UploadQueue<K> {
  let limit = clampConcurrency(options.limit ?? DEFAULT_CONCURRENCY)
  const waiting: K[] = []
  const running = new Map<K, RunningUpload>()

  const settle = (key: K, error?: Error) => {
    running.delete(key)
    try {
      options.onSettled(key, error)
    } finally {
      pump()
    }
  }

  const begin = (key: K) => {
    let upload: RunningUpload
    try {
      upload = options.start(key)
    } catch (error) {
      options.onSettled(key, toError(error))
      return
    }
    running.set(key, upload)
    options.onStart?.(key)
    upload.result.then(
      () => settle(key),
      (error: unknown) => settle(key, toError(error))
    )
  }

  const pump = () => {
    while (running.size < limit && waiting.length > 0) {
      begin(waiting.shift() as K)
    }
  }

  return {
    enqueue: (key) => {
      waiting.push(key)
      pump()
    },
    cancel: (key) => {
      const index = waiting.indexOf(key)
      if (index >= 0) {
        waiting.splice(index, 1)
        options.onDequeued(key)
        return
      }
      running.get(key)?.cancel()
    },
    cancelAll: () => {
      const dropped = waiting.splice(0)
      dropped.forEach(options.onDequeued)
      running.forEach((upload) => upload.cancel())
    },
    setLimit: (next) => {
      limit = clampConcurrency(next)
      pump()
    },
    get active() {
      return running.size
    },
    get queued() {
      return waiting.length
    },
  }
}

function toError(error: unknown) {
  return error instanceof Error ? error : new Error(String(error))
}
