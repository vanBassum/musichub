import type { CatalogArtist } from "@/lib/catalog-api"

// A track's artists, each opening its artist page. Stops the click, so it can sit in a
// row that selects the track.
export function ArtistLinks({
  artists,
  onSelect,
}: {
  artists: CatalogArtist[]
  onSelect: (artistId: number) => void
}) {
  return artists.map((artist, index) => (
    <span key={artist.artistId}>
      {index > 0 && ", "}
      <button
        type="button"
        className="rounded-sm hover:underline focus-visible:ring-2 focus-visible:ring-ring/40 focus-visible:outline-none"
        onClick={(event) => {
          event.stopPropagation()
          onSelect(artist.artistId)
        }}
      >
        {artist.name}
      </button>
    </span>
  ))
}
