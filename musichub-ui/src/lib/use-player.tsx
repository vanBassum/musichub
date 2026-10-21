import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react"

import {
  artistNames,
  trackTitle,
  type CatalogTrack,
} from "@/lib/catalog-api"
import {
  clearExcept,
  insertAfter,
  moveEntry as moveQueueEntry,
  neighbour,
  removeEntry as removeQueueEntry,
  type Keyed,
} from "@/lib/player-queue"

// "loading" is the time between asking for the audio and hearing it (or failing).
export type PlaybackStatus =
  | "idle"
  | "loading"
  | "playing"
  | "paused"
  | "ended"
  | "error"

// What is loaded in the player: always one Stored file, played either directly from Files
// (no catalog metadata, so its original filename is the title) or as the audio of a catalog
// track. `id` is the Stored file's id in both cases, so one file is one loaded source.
export interface PlayerSource {
  id: string
  title: string
  // The artists of a catalog track; null for a file.
  subtitle: string | null
}

// One place in the current playback queue (ephemeral: it lives in this tab only).
export interface QueueEntry extends Keyed {
  source: PlayerSource
}

// A catalog track that can be played: its linked Stored file is known.
export type PlayableTrack = CatalogTrack & { audioFileId: number }

export function isPlayable(track: CatalogTrack): track is PlayableTrack {
  return track.audioFileId !== null
}

// Whether a Stored file is the one loaded (and not failed), and whether audio is meant to
// be heard: the one owner of both decisions for every play button and player.
export function isLoaded(
  source: PlayerSource | null,
  status: PlaybackStatus,
  fileId: string
) {
  return source?.id === fileId && status !== "error"
}

export function isAudible(status: PlaybackStatus) {
  return status === "playing" || status === "loading"
}

export interface PlayerState {
  source: PlayerSource | null
  status: PlaybackStatus
  currentTime: number
  duration: number
  error: string | null
  // The current playback queue and the entry that is loaded from it (null when nothing
  // is loaded, or when the loaded source is a Files file, which is not queued).
  queue: QueueEntry[]
  currentKey: number | null
  hasNext: boolean
  playFile: (file: { id: string; originalFilename: string }) => void
  // Starts the track right after the current entry; the rest of the queue stays.
  playNow: (track: PlayableTrack) => void
  playNext: (track: PlayableTrack) => void
  enqueue: (track: PlayableTrack) => void
  playEntry: (key: number) => void
  removeEntry: (key: number) => void
  moveEntry: (key: number, offset: -1 | 1) => void
  clearQueue: () => void
  next: () => void
  previous: () => void
  toggle: () => void
  seek: (seconds: number) => void
}

const PlayerContext = createContext<PlayerState | null>(null)

// MediaError codes: 4 is "the source is missing or its format isn't supported".
function describeMediaError(error: MediaError | null) {
  return error?.code === 4
    ? "This file can't be played: the browser doesn't support its format, or the file is unavailable."
    : "The file could not be played."
}

// A catalog track as the player shows and loads it.
function trackSource(track: PlayableTrack): PlayerSource {
  return {
    id: String(track.audioFileId),
    title: trackTitle(track),
    subtitle: artistNames(track) || null,
  }
}

// "Previous" restarts the track instead when it is already this far in.
const RESTART_AFTER_SECONDS = 3

// The one HTMLAudioElement of the app, outside React so no remount can restart it.
const audio = new Audio()
audio.preload = "metadata"

// Mount it above the layouts, so playback state survives navigation, overlays and the
// desktop/mobile layout swap.
export function PlayerProvider({ children }: { children: ReactNode }) {
  const [source, setSource] = useState<PlayerSource | null>(null)
  const [status, setStatus] = useState<PlaybackStatus>("idle")
  const [currentTime, setCurrentTime] = useState(0)
  const [duration, setDuration] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [queue, setQueue] = useState<QueueEntry[]>([])
  const [currentKey, setCurrentKey] = useState<number | null>(null)
  const nextKey = useRef(0)
  // The ended listener is registered once; it calls whatever "next" is by then.
  const advance = useRef<() => void>(() => {})

  useEffect(() => {
    const fail = () => {
      setError(describeMediaError(audio.error))
      setStatus("error")
    }
    const settle = () =>
      setStatus((current) =>
        current === "loading" ? (audio.paused ? "paused" : "playing") : current,
      )
    const listeners: Record<string, () => void> = {
      // Loading only counts while audio is meant to play; a seek or load while paused
      // fires no `playing` later, so canplay/seeked settle it back to paused.
      loadstart: () => {
        if (!audio.paused) setStatus("loading")
      },
      waiting: () => {
        if (!audio.paused) setStatus("loading")
      },
      canplay: settle,
      seeked: settle,
      playing: () => setStatus("playing"),
      pause: () => {
        // A stopped player has no source left to be paused on.
        if (!audio.ended && !audio.error && audio.getAttribute("src"))
          setStatus("paused")
      },
      ended: () => {
        setStatus("ended")
        advance.current()
      },
      error: fail,
      timeupdate: () => setCurrentTime(audio.currentTime),
      durationchange: () =>
        setDuration(Number.isFinite(audio.duration) ? audio.duration : 0),
    }
    for (const [name, listener] of Object.entries(listeners))
      audio.addEventListener(name, listener)
    return () => {
      for (const [name, listener] of Object.entries(listeners))
        audio.removeEventListener(name, listener)
    }
  }, [])

  const start = useCallback(() => {
    audio.play().catch((reason: unknown) => {
      // A newer load interrupts this one, and a media error is reported by its event.
      if (reason instanceof DOMException && reason.name === "AbortError") return
      if (audio.error) return
      setError("The file could not be played.")
      setStatus("error")
    })
  }, [])

  // The one place a Stored file is loaded, whoever asks: Files, a catalog track or a queue
  // entry (`key`; null for a file that is not queued).
  const play = useCallback(
    (next: PlayerSource, key: number | null) => {
      setCurrentKey(key)
      if (isLoaded(source, status, next.id)) {
        // Another entry of the same file starts over, too.
        if (status === "ended" || key !== currentKey) audio.currentTime = 0
        start()
        return
      }
      setSource(next)
      setStatus("loading")
      setError(null)
      setCurrentTime(0)
      setDuration(0)
      audio.src = `/api/files/${encodeURIComponent(next.id)}/audio`
      start()
    },
    [source, status, currentKey, start]
  )

  // Unloads the player: nothing is left to play.
  const stop = useCallback(() => {
    audio.pause()
    audio.removeAttribute("src")
    audio.load()
    setSource(null)
    setCurrentKey(null)
    setStatus("idle")
    setError(null)
    setCurrentTime(0)
    setDuration(0)
  }, [])

  const playFile = useCallback(
    (file: { id: string; originalFilename: string }) =>
      play({ id: file.id, title: file.originalFilename, subtitle: null }, null),
    [play]
  )

  const newEntry = useCallback(
    (track: PlayableTrack): QueueEntry => ({
      key: nextKey.current++,
      source: trackSource(track),
    }),
    []
  )

  const enqueue = useCallback(
    (track: PlayableTrack) =>
      setQueue((current) => [...current, newEntry(track)]),
    [newEntry]
  )

  const playNext = useCallback(
    (track: PlayableTrack) =>
      setQueue((current) => insertAfter(current, currentKey, newEntry(track))),
    [currentKey, newEntry]
  )

  const playNow = useCallback(
    (track: PlayableTrack) => {
      const entry = newEntry(track)
      setQueue((current) => insertAfter(current, currentKey, entry))
      play(entry.source, entry.key)
    },
    [currentKey, newEntry, play]
  )

  const playEntry = useCallback(
    (key: number) => {
      const entry = queue.find((candidate) => candidate.key === key)
      if (entry) play(entry.source, entry.key)
    },
    [queue, play]
  )

  const following = neighbour(queue, currentKey, 1)
  const preceding = neighbour(queue, currentKey, -1)

  // Removing the loaded entry moves on to the one after it while audio is playing, and
  // otherwise unloads the player; removing any other entry never touches playback.
  const removeEntry = useCallback(
    (key: number) => {
      if (key === currentKey) {
        if (following && isAudible(status)) play(following.source, following.key)
        else stop()
      }
      setQueue((current) => removeQueueEntry(current, key))
    },
    [currentKey, following, status, play, stop]
  )

  const moveEntry = useCallback(
    (key: number, offset: -1 | 1) =>
      setQueue((current) => moveQueueEntry(current, key, offset)),
    []
  )

  // The loaded entry stays, so clearing never cuts the audio off.
  const clearQueue = useCallback(
    () => setQueue((current) => clearExcept(current, currentKey)),
    [currentKey]
  )

  const next = useCallback(() => {
    if (following) play(following.source, following.key)
  }, [following, play])

  useEffect(() => {
    advance.current = next
  }, [next])

  const toggle = useCallback(() => {
    if (!source || status === "error") return
    if (audio.paused || audio.ended) {
      if (audio.ended) audio.currentTime = 0
      start()
    } else {
      audio.pause()
    }
  }, [source, status, start])

  const seek = useCallback(
    (seconds: number) => {
      if (!source || status === "error") return
      audio.currentTime = seconds
      setCurrentTime(seconds)
    },
    [source, status]
  )

  const previous = useCallback(() => {
    if (!source) return
    if (preceding && audio.currentTime <= RESTART_AFTER_SECONDS)
      play(preceding.source, preceding.key)
    else seek(0)
  }, [source, preceding, play, seek])

  const value = useMemo(
    () => ({
      source,
      status,
      currentTime,
      duration,
      error,
      queue,
      currentKey,
      hasNext: following !== undefined,
      playFile,
      playNow,
      playNext,
      enqueue,
      playEntry,
      removeEntry,
      moveEntry,
      clearQueue,
      next,
      previous,
      toggle,
      seek,
    }),
    [
      source,
      status,
      currentTime,
      duration,
      error,
      queue,
      currentKey,
      following,
      playFile,
      playNow,
      playNext,
      enqueue,
      playEntry,
      removeEntry,
      moveEntry,
      clearQueue,
      next,
      previous,
      toggle,
      seek,
    ]
  )

  return <PlayerContext.Provider value={value}>{children}</PlayerContext.Provider>
}

export function usePlayer(): PlayerState {
  const player = useContext(PlayerContext)
  if (!player) throw new Error("usePlayer needs a PlayerProvider above it.")
  return player
}
