export type TrackStatus = "ok" | "unknown" | "new" | "to-check"

export interface Track {
  id: string
  title: string
  artist: string
  album: string
  year: number
  durationSeconds: number
  status: TrackStatus
}

export const trackStatuses: { value: TrackStatus; label: string }[] = [
  { value: "ok", label: "OK" },
  { value: "unknown", label: "Unknown" },
  { value: "new", label: "New" },
  { value: "to-check", label: "To check" },
]

export const mockTracks: Track[] = [
  {
    id: "1",
    title: "Come as You Are",
    artist: "Nirvana",
    album: "Nevermind",
    year: 1991,
    durationSeconds: 219,
    status: "ok",
  },
  {
    id: "2",
    title: "Love Rollercoaster",
    artist: "Ohio Players",
    album: "Honey",
    year: 1975,
    durationSeconds: 289,
    status: "ok",
  },
  {
    id: "3",
    title: "Pop Muzik",
    artist: "M",
    album: "New York • London • Paris • Munich",
    year: 1979,
    durationSeconds: 202,
    status: "ok",
  },
  {
    id: "4",
    title: "Janie's Got a Gun",
    artist: "Aerosmith",
    album: "Big Ones",
    year: 1994,
    durationSeconds: 327,
    status: "to-check",
  },
  {
    id: "5",
    title: "It's My Life",
    artist: "Dr. Alban",
    album: "It's My Life (Single)",
    year: 1992,
    durationSeconds: 240,
    status: "ok",
  },
  {
    id: "6",
    title: "Blue Monday",
    artist: "New Order",
    album: "Power, Corruption & Lies",
    year: 1983,
    durationSeconds: 449,
    status: "ok",
  },
  {
    id: "7",
    title: "Smalltown Boy",
    artist: "Bronski Beat",
    album: "The Age of Consent",
    year: 1984,
    durationSeconds: 303,
    status: "ok",
  },
  {
    id: "8",
    title: "Dig Em Up!",
    artist: "The Variations",
    album: "Dig Em Up! (Single)",
    year: 1970,
    durationSeconds: 166,
    status: "new",
  },
  {
    id: "9",
    title: "The Model",
    artist: "Kraftwerk",
    album: "The Man-Machine",
    year: 1978,
    durationSeconds: 222,
    status: "ok",
  },
  {
    id: "10",
    title: "West End Girls",
    artist: "Pet Shop Boys",
    album: "Please",
    year: 1986,
    durationSeconds: 246,
    status: "ok",
  },
  {
    id: "11",
    title: "Love Spirals Downwards",
    artist: "The Jesus and Mary Chain",
    album: "Automatic",
    year: 1989,
    durationSeconds: 357,
    status: "unknown",
  },
  {
    id: "12",
    title: "Just Like Heaven",
    artist: "The Cure",
    album: "Kiss Me, Kiss Me, Kiss Me",
    year: 1987,
    durationSeconds: 212,
    status: "ok",
  },
]

// The player is a placeholder: a fixed queue of the mock tracks, with the first one current.
export const mockQueue: Track[] = mockTracks
export const mockNowPlayingId = mockQueue[0].id
