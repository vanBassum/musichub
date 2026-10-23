import type { JobStateEntry } from "@/lib/jobs-api"

export type EventTone = "neutral" | "success" | "warning" | "error"

// One timestamped line of a run's log. `key` is unique within the log.
export interface RunEvent {
  key: string
  at: string
  text: string
  tone: EventTone
}

// Oldest first; lines with the same instant keep the order they were given in.
export function chronological(events: RunEvent[]): RunEvent[] {
  return events
    .map((event) => ({ event, time: new Date(event.at).getTime() }))
    .sort((a, b) => a.time - b.time)
    .map(({ event }) => event)
}

const stateTones: Record<string, EventTone> = {
  Succeeded: "success",
  Failed: "error",
}

// Hangfire's state changes of the run (queued, started, finished, failed with its reason).
export function stateEvents(history: JobStateEntry[]): RunEvent[] {
  return history.map((entry, index) => ({
    key: `state-${index}`,
    at: entry.at,
    text: entry.reason ? `${entry.state}: ${entry.reason}` : entry.state,
    tone: stateTones[entry.state] ?? "neutral",
  }))
}
