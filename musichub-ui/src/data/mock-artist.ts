import type { Artist, ArtistAlbum, PopularTrack } from "@/lib/artist"

// Demo data for the artist detail page. Nothing here is real or comes from the API.
export const mockArtist: Artist = {
  name: "Demo Artist",
  origin: "local",
  inCatalog: true,
  genres: ["Alternative", "Indie Rock"],
  description:
    "A made-up artist that stands in for real data. The description, tracks and albums on this page are demo content.",
}

export const mockPopularTracks: PopularTrack[] = [
  { id: "t1", title: "Demo Track One", album: "Demo Album A", year: 2023, availability: "available" },
  { id: "t2", title: "Demo Track Two", album: "Demo Album B", year: 2022, availability: "missing" },
  { id: "t3", title: "Demo Track Three", album: "Demo Album B", year: 2022, availability: "missing" },
  { id: "t4", title: "Demo Track Four", album: "Demo Album C", year: 2021, availability: "available" },
  { id: "t5", title: "Demo Track Five", album: "Demo Album A", year: 2023, availability: "external" },
]

export const mockAlbums: ArtistAlbum[] = [
  { id: "a1", title: "Demo Album A", year: 2023, availability: "available" },
  { id: "a2", title: "Demo Album B", year: 2022, availability: "missing" },
  { id: "a3", title: "Demo Album C", year: 2021, availability: "available" },
  { id: "a4", title: "Demo Album D", year: 2019, availability: "external" },
]
