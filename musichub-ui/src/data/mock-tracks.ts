export interface Track {
  id: string
  title: string
  artist: string
  album: string
  year: number
  duration: string
  format: string
  genre: string
}

export const mockTracks: Track[] = [
  { id: "1", title: "Midnight Drive", artist: "Neon Harbor", album: "Night Signals", year: 2019, duration: "3:42", format: "FLAC 24/96", genre: "Synthwave" },
  { id: "2", title: "Paper Boats", artist: "Elin Voss", album: "Small Rivers", year: 2021, duration: "4:05", format: "FLAC 16/44.1", genre: "Folk" },
  { id: "3", title: "Static Bloom", artist: "Neon Harbor", album: "Night Signals", year: 2019, duration: "5:18", format: "FLAC 24/96", genre: "Synthwave" },
  { id: "4", title: "Golden Hour", artist: "The Lowlands", album: "Open Fields", year: 2017, duration: "3:27", format: "MP3 320", genre: "Indie" },
  { id: "5", title: "Echoes in Glass", artist: "Mira Castell", album: "Transparent", year: 2023, duration: "6:11", format: "FLAC 24/48", genre: "Ambient" },
  { id: "6", title: "Back to Zero", artist: "The Lowlands", album: "Open Fields", year: 2017, duration: "3:55", format: "MP3 320", genre: "Indie" },
  { id: "7", title: "Saltwater", artist: "Elin Voss", album: "Small Rivers", year: 2021, duration: "2:58", format: "FLAC 16/44.1", genre: "Folk" },
  { id: "8", title: "Overclock", artist: "Kilo Pulse", album: "Voltage", year: 2020, duration: "4:44", format: "AAC 256", genre: "Electronic" },
  { id: "9", title: "Slow Tide", artist: "Mira Castell", album: "Transparent", year: 2023, duration: "7:02", format: "FLAC 24/48", genre: "Ambient" },
  { id: "10", title: "City Lights Fade", artist: "Kilo Pulse", album: "Voltage", year: 2020, duration: "3:36", format: "AAC 256", genre: "Electronic" },
  { id: "11", title: "Red Letter Day", artist: "June & the Wolves", album: "Red Letter Day", year: 2015, duration: "3:12", format: "MP3 256", genre: "Rock" },
  { id: "12", title: "Hollow Crown", artist: "June & the Wolves", album: "Red Letter Day", year: 2015, duration: "4:29", format: "MP3 256", genre: "Rock" },
]
