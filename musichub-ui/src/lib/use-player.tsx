import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react"

import {
  artistNames,
  trackTitle,
  type CatalogTrack,
} from "@/lib/catalog-api"

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
  playFile: (file: { id: string; originalFilename: string }) => void
  playTrack: (track: PlayableTrack) => void
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
        if (!audio.ended && !audio.error) setStatus("paused")
      },
      ended: () => setStatus("ended"),
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

  // The one place a Stored file is loaded, whoever asks: Files or a catalog track.
  const play = useCallback(
    (next: PlayerSource) => {
      if (isLoaded(source, status, next.id)) {
        if (status === "ended") audio.currentTime = 0
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
    [source, status, start]
  )

  const playFile = useCallback(
    (file: { id: string; originalFilename: string }) =>
      play({
        id: file.id,
        title: file.originalFilename,
        subtitle: null,
      }),
    [play]
  )

  const playTrack = useCallback(
    (track: PlayableTrack) =>
      play({
        id: String(track.audioFileId),
        title: trackTitle(track),
        subtitle: artistNames(track) || null,
      }),
    [play]
  )

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

  const value = useMemo(
    () => ({
      source,
      status,
      currentTime,
      duration,
      error,
      playFile,
      playTrack,
      toggle,
      seek,
    }),
    [
      source,
      status,
      currentTime,
      duration,
      error,
      playFile,
      playTrack,
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
