// The ordering rules of the current playback queue, apart from React and audio. Entries are
// identified by a `key` of their own, so the same Stored file can be queued twice and the
// current entry survives reordering and edits around it.
export interface Keyed {
  key: number
}

function indexOfKey(queue: Keyed[], key: number | null) {
  return key === null ? -1 : queue.findIndex((entry) => entry.key === key)
}

// Directly after the entry `afterKey`; at the front when there is no such entry.
export function insertAfter<T extends Keyed>(
  queue: T[],
  afterKey: number | null,
  entry: T
) {
  const at = indexOfKey(queue, afterKey) + 1
  return [...queue.slice(0, at), entry, ...queue.slice(at)]
}

export function removeEntry<T extends Keyed>(queue: T[], key: number) {
  return queue.filter((entry) => entry.key !== key)
}

// Moves an entry up (-1) or down (+1); the queue is returned as it was at either end.
export function moveEntry<T extends Keyed>(
  queue: T[],
  key: number,
  offset: -1 | 1
) {
  const from = indexOfKey(queue, key)
  const to = from + offset
  if (from < 0 || to < 0 || to >= queue.length) return queue
  const moved = [...queue]
  ;[moved[from], moved[to]] = [moved[to], moved[from]]
  return moved
}

// The entry before (-1) or after (+1) the entry `key`; undefined when `key` is not queued
// or it is the first/last one.
export function neighbour<T extends Keyed>(
  queue: T[],
  key: number | null,
  offset: -1 | 1
) {
  const at = indexOfKey(queue, key)
  return at < 0 ? undefined : queue[at + offset]
}

// Clearing keeps the entry that is current, so audio is never cut off by it.
export function clearExcept<T extends Keyed>(queue: T[], key: number | null) {
  return queue.filter((entry) => entry.key === key)
}
