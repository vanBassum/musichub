// Placeholder data for the player panel only. The library shows the real catalog.
export interface Track {
  id: string
  title: string
  artist: string
  album: string
  year: number
  durationSeconds: number
}

export const mockTracks: Track[] = [
  { id: "1", title: "Come as You Are", artist: "Nirvana", album: "Nevermind", year: 1991, durationSeconds: 219 },
  { id: "2", title: "Love Rollercoaster", artist: "Ohio Players", album: "Honey", year: 1975, durationSeconds: 289 },
  { id: "3", title: "Pop Muzik", artist: "M", album: "New York • London • Paris • Munich", year: 1979, durationSeconds: 202 },
  { id: "4", title: "Janie's Got a Gun", artist: "Aerosmith", album: "Big Ones", year: 1994, durationSeconds: 327 },
  { id: "5", title: "It's My Life", artist: "Dr. Alban", album: "It's My Life (Single)", year: 1992, durationSeconds: 240 },
  { id: "6", title: "Blue Monday", artist: "New Order", album: "Power, Corruption & Lies", year: 1983, durationSeconds: 449 },
  { id: "7", title: "Smalltown Boy", artist: "Bronski Beat", album: "The Age of Consent", year: 1984, durationSeconds: 303 },
  { id: "8", title: "Dig Em Up!", artist: "The Variations", album: "Dig Em Up! (Single)", year: 1970, durationSeconds: 166 },
]

// The player is a placeholder: a fixed queue of the mock tracks, with the first one current.
export const mockQueue: Track[] = mockTracks
export const mockNowPlayingId = mockQueue[0].id
