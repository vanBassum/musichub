// Albums need a catalog model for albums and releases, which is not designed yet, so
// the section says so instead of showing nothing or made-up albums.
export function ArtistAlbums() {
  return (
    <section aria-labelledby="albums-heading" className="min-w-0 space-y-3">
      <h2 id="albums-heading" className="text-lg font-semibold">
        Albums
      </h2>
      <p className="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
        Albums are not available yet: MusicHub does not model albums or releases.
      </p>
    </section>
  )
}
