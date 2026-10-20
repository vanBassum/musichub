import { describe, expect, it } from "vitest"

import { appendUpload, type Upload } from "./use-files"
import {
  clampConcurrency,
  createUploadQueue,
  type RunningUpload,
} from "./upload-queue"

// A fake upload the test settles by hand, like an XHR the server answers later.
interface Fake extends RunningUpload {
  succeed: () => void
  fail: (message: string) => void
  cancelled: boolean
}

function harness(limit?: number) {
  const fakes = new Map<number, Fake>()
  const started: number[] = []
  const settled: [number, string | undefined][] = []
  const dequeued: number[] = []
  let concurrent = 0
  let peak = 0

  const queue = createUploadQueue<number>({
    limit,
    start: (key) => {
      let resolve!: () => void
      let reject!: (error: Error) => void
      const result = new Promise<void>((res, rej) => {
        resolve = res
        reject = rej
      })
      const fake: Fake = {
        result,
        cancelled: false,
        succeed: resolve,
        fail: (message) => reject(new Error(message)),
        cancel: () => {
          fake.cancelled = true
          reject(new Error("cancelled"))
        },
      }
      fakes.set(key, fake)
      return fake
    },
    onStart: (key) => {
      started.push(key)
      concurrent++
      peak = Math.max(peak, concurrent)
    },
    onSettled: (key, error) => {
      concurrent--
      settled.push([key, error?.message])
    },
    onDequeued: (key) => dequeued.push(key),
  })

  const enqueue = (count: number) => {
    for (let key = 0; key < count; key++) queue.enqueue(key)
  }
  // Lets the promise reactions run.
  const flush = () => new Promise<void>((resolve) => setTimeout(resolve, 0))

  return {
    queue,
    fakes,
    started,
    settled,
    dequeued,
    enqueue,
    flush,
    peak: () => peak,
  }
}

describe("clampConcurrency", () => {
  it("keeps 1 to 10 and falls back to 5 for nonsense", () => {
    expect(clampConcurrency(0)).toBe(1)
    expect(clampConcurrency(11)).toBe(10)
    expect(clampConcurrency(3.9)).toBe(3)
    expect(clampConcurrency(Number.NaN)).toBe(5)
  })
})

describe("upload list order", () => {
  it("shows a batch in the order the queue starts it", () => {
    const h = harness()
    let shown: Upload[] = []
    for (let key = 0; key < 10; key++) {
      shown = appendUpload(shown, {
        key,
        name: `file-${key}`,
        size: 1,
        loaded: 0,
        state: "queued",
      })
      h.queue.enqueue(key)
    }

    expect(shown.map((u) => u.key).slice(0, 5)).toEqual(h.started)
  })
})

describe("upload queue", () => {
  it("runs at most the default 5 of 20 files and queues the rest in order", () => {
    const h = harness()
    h.enqueue(20)

    expect(h.started).toEqual([0, 1, 2, 3, 4])
    expect(h.queue.active).toBe(5)
    expect(h.queue.queued).toBe(15)
  })

  it("starts the next file when one finishes, even out of order", async () => {
    const h = harness()
    h.enqueue(20)

    h.fakes.get(3)!.succeed()
    await h.flush()
    expect(h.started).toEqual([0, 1, 2, 3, 4, 5])
    expect(h.settled).toEqual([[3, undefined]])

    h.fakes.get(0)!.succeed()
    h.fakes.get(5)!.succeed()
    await h.flush()
    expect(h.started).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8])
    expect(h.queue.active).toBe(5)
  })

  it("never exceeds the limit while draining all 20 files", async () => {
    const h = harness()
    h.enqueue(20)

    for (let done = 0; done < 20; done++) {
      const next = [...h.fakes.keys()].find(
        (key) => !h.settled.some(([settledKey]) => settledKey === key)
      )!
      h.fakes.get(next)!.succeed()
      await h.flush()
    }

    expect(h.peak()).toBe(5)
    expect(h.settled).toHaveLength(20)
    expect(h.queue.active).toBe(0)
    expect(h.queue.queued).toBe(0)
  })

  it("releases the slot when an upload fails", async () => {
    const h = harness(1)
    h.enqueue(3)

    h.fakes.get(0)!.fail("HTTP 500")
    await h.flush()
    expect(h.settled).toEqual([[0, "HTTP 500"]])
    expect(h.started).toEqual([0, 1])

    h.fakes.get(1)!.succeed()
    await h.flush()
    expect(h.started).toEqual([0, 1, 2])
  })

  it("releases the slot when start throws", () => {
    const settled: [number, string | undefined][] = []
    const queue = createUploadQueue<number>({
      limit: 1,
      start: (key) => {
        if (key === 0) throw new Error("no crypto")
        return { result: new Promise<void>(() => {}), cancel: () => {} }
      },
      onSettled: (key, error) => settled.push([key, error?.message]),
      onDequeued: () => {},
    })

    queue.enqueue(0)
    queue.enqueue(1)

    expect(settled).toEqual([[0, "no crypto"]])
    expect(queue.active).toBe(1)
  })

  it("cancels one running file without touching the others", async () => {
    const h = harness(2)
    h.enqueue(4)

    h.queue.cancel(0)
    await h.flush()

    expect(h.fakes.get(0)!.cancelled).toBe(true)
    expect(h.fakes.get(1)!.cancelled).toBe(false)
    expect(h.settled).toEqual([[0, "cancelled"]])
    expect(h.started).toEqual([0, 1, 2])
  })

  it("cancels one queued file without starting it", async () => {
    const h = harness(1)
    h.enqueue(3)

    h.queue.cancel(1)
    expect(h.dequeued).toEqual([1])

    h.fakes.get(0)!.succeed()
    await h.flush()
    expect(h.started).toEqual([0, 2])
  })

  it("cancels the batch: drops the queue, aborts running, keeps stored files", async () => {
    const h = harness(3)
    h.enqueue(8)
    h.fakes.get(0)!.succeed()
    await h.flush()
    expect(h.started).toEqual([0, 1, 2, 3])

    h.queue.cancelAll()
    await h.flush()

    expect(h.dequeued).toEqual([4, 5, 6, 7])
    expect([1, 2, 3].map((key) => h.fakes.get(key)!.cancelled)).toEqual([
      true,
      true,
      true,
    ])
    expect(h.fakes.get(0)!.cancelled).toBe(false)
    expect(h.started).toEqual([0, 1, 2, 3])
    expect(h.settled[0]).toEqual([0, undefined])
    expect(h.queue.active).toBe(0)
    expect(h.queue.queued).toBe(0)
  })

  it("accepts new files after a batch cancel", async () => {
    const h = harness(1)
    h.enqueue(2)
    h.queue.cancelAll()
    await h.flush()

    h.queue.enqueue(9)
    expect(h.started).toEqual([0, 9])
  })

  it("lowering the limit does not interrupt running uploads or start new ones", async () => {
    const h = harness(5)
    h.enqueue(10)

    h.queue.setLimit(2)
    expect(h.queue.active).toBe(5)

    for (const key of [0, 1, 2]) h.fakes.get(key)!.succeed()
    await h.flush()
    expect(h.queue.active).toBe(2)
    expect(h.started).toEqual([0, 1, 2, 3, 4])

    h.fakes.get(3)!.succeed()
    await h.flush()
    expect(h.started).toEqual([0, 1, 2, 3, 4, 5])
    expect(h.queue.active).toBe(2)
  })

  it("raising the limit starts queued files at once", () => {
    const h = harness(1)
    h.enqueue(6)

    h.queue.setLimit(4)

    expect(h.started).toEqual([0, 1, 2, 3])
    expect(h.queue.queued).toBe(2)
  })
})
