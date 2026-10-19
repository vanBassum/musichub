import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react"

// "loading" is the time between asking for the audio and hearing it (or failing).
export type PlaybackStatus =
  | "idle"
  | "loading"
  | "playing"
  | "paused"
  | "ended"
  | "error"

// What is loaded in the player. Only Stored files for now; a catalog track would be
// another kind. A file has no catalog metadata, so its original filename is the title.
export interface PlayerSource {
  kind: "file"
  id: string
  title: string
}

export interface PlayerState {
  source: PlayerSource | null
  status: PlaybackStatus
  currentTime: number
  duration: number
  error: string | null
  playFile: (file: { id: string; originalFilename: string }) => void
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
    const listeners: Record<string, () => void> = {
      loadstart: () => setStatus("loading"),
      waiting: () => setStatus("loading"),
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

  const playFile = useCallback(
    (file: { id: string; originalFilename: string }) => {
      if (source?.id === file.id && status !== "error") {
        if (status === "ended") audio.currentTime = 0
        start()
        return
      }
      setSource({ kind: "file", id: file.id, title: file.originalFilename })
      setStatus("loading")
      setError(null)
      setCurrentTime(0)
      setDuration(0)
      audio.src = `/api/files/${encodeURIComponent(file.id)}/audio`
      start()
    },
    [source, status, start]
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
      toggle,
      seek,
    }),
    [source, status, currentTime, duration, error, playFile, toggle, seek]
  )

  return <PlayerContext.Provider value={value}>{children}</PlayerContext.Provider>
}

export function usePlayer(): PlayerState {
  const player = useContext(PlayerContext)
  if (!player) throw new Error("usePlayer needs a PlayerProvider above it.")
  return player
}
