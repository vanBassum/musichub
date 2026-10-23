import { ArrowLeft } from "lucide-react"
import { useCallback, useId, useState } from "react"

import { AddToLibraryButton, useAddState } from "@/components/add-to-library-button"
import { FailureAlert, LoadingRows } from "@/components/result-states"
import { Button } from "@/components/ui/button"
import {
  albumSubtitle,
  editionLabel,
  editionTrackToExternal,
  fetchAlbumEditions,
  fetchEditionTracks,
  trackArtistLine,
  type EditionTracks,
  type ExternalAlbum,
} from "@/lib/album-api"
import { addExternalTrackToLibrary } from "@/lib/catalog-api"
import { providerLabel } from "@/lib/artist"
import { useLoader } from "@/lib/use-loader"

// The tracks of the edition that was chosen, with what is missing from the list said
// plainly. "Add all" adds exactly these tracks to the library, one by one: a track the
// provider didn't name or identify is skipped, never filled in.
function EditionTrackList({
  editionId,
  album,
}: {
  editionId: string
  album: ExternalAlbum
}) {
  const fetcher = useCallback(
    (signal: AbortSignal) => fetchEditionTracks(editionId, signal),
    [editionId]
  )
  const { data, error, refreshing, refresh } = useLoader(fetcher)
  if (refreshing && !data) return <LoadingRows label="Loading tracklist" />
  if (!data) {
    return (
      <FailureAlert
        message={error ?? "The tracklist could not be loaded."}
        onRetry={() => void refresh()}
      />
    )
  }
  return <Tracklist list={data} album={album} />
}

function Tracklist({
  list,
  album,
}: {
  list: EditionTracks
  album: ExternalAlbum
}) {
  const rows = list.tracks.map((track) => ({
    track,
    external: editionTrackToExternal(track, album.provider),
  }))
  const addable = rows.flatMap((row) => (row.external ? [row.external] : []))
  const addAll = () =>
    Promise.all(addable.map((track) => addExternalTrackToLibrary(track)))

  return (
    <div className="flex flex-col gap-2">
      {list.state !== "Complete" && (
        <p role="status" className="px-2 text-xs text-muted-foreground">
          {list.state === "Missing"
            ? "The provider has no tracklist for this edition."
            : "This tracklist is incomplete."}{" "}
          {list.problems.join(" ")}
        </p>
      )}
      <ol className="flex flex-col">
        {rows.map(({ track, external }) => {
          const artists = trackArtistLine(track, album)
          return (
            <li
              key={track.position}
              className="flex items-center gap-2 px-2 py-1.5 text-sm"
            >
              <span className="w-6 shrink-0 text-right text-xs text-muted-foreground">
                {track.number ?? track.position}
              </span>
              <div className="min-w-0 flex-1">
                <div className="truncate font-medium">
                  {track.title ?? "Untitled track"}
                </div>
                {artists && (
                  <div className="truncate text-xs text-muted-foreground">
                    {artists}
                  </div>
                )}
              </div>
              {external && (
                <AddToLibraryButton
                  onAdd={() => addExternalTrackToLibrary(external)}
                />
              )}
            </li>
          )
        })}
      </ol>
      {addable.length > 0 && (
        <div className="px-2">
          {/* Remounted per edition, so it never claims another edition was added. */}
          <AddAllButton
            key={list.edition.externalId}
            count={addable.length}
            onAdd={addAll}
          />
        </div>
      )}
    </div>
  )
}

function AddAllButton({
  count,
  onAdd,
}: {
  count: number
  onAdd: () => Promise<unknown>
}) {
  const { state, press } = useAddState(onAdd)
  return (
    <Button
      variant="outline"
      disabled={state === "saving" || state === "added"}
      onClick={press}
    >
      {state === "added"
        ? "Added to library"
        : state === "error"
          ? "Retry add all to library"
          : `Add ${count} tracks to library`}
    </Button>
  )
}

// One album: its editions to choose from, then the chosen edition's tracklist. The
// edition is always the user's explicit choice (or the single one the provider has);
// nothing substitutes another. Reading stores nothing.
export function AlbumDetail({
  album,
  onBack,
}: {
  album: ExternalAlbum
  onBack: () => void
}) {
  const fetcher = useCallback(
    (signal: AbortSignal) => fetchAlbumEditions(album.externalId, signal),
    [album.externalId]
  )
  const { data, error, refreshing, refresh } = useLoader(fetcher)
  const [chosen, setChosen] = useState<string | null>(null)
  const name = useId()
  // The provider's suggestion stands only until the user picks another edition.
  const selected = chosen ?? data?.suggestion.editionId ?? null

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto">
      <div className="flex items-center gap-2">
        <Button variant="ghost" size="sm" onClick={onBack}>
          <ArrowLeft data-icon="inline-start" />
          Back to results
        </Button>
      </div>
      <div className="px-2">
        <h2 className="text-base font-semibold">{album.title}</h2>
        <p className="text-xs text-muted-foreground">
          {albumSubtitle(album)} · {providerLabel(album.provider)}
        </p>
      </div>
      {refreshing && !data ? (
        <LoadingRows label="Loading editions" />
      ) : !data ? (
        <FailureAlert
          message={error ?? "The editions could not be loaded."}
          onRetry={() => void refresh()}
        />
      ) : (
        <>
          <fieldset className="flex flex-col gap-0.5">
            <legend className="px-2 pb-1 text-sm font-medium">
              Choose an edition
            </legend>
            {data.editions.map((edition) => (
              <label
                key={edition.externalId}
                className="flex min-h-9 items-center gap-2 rounded-md px-2 text-sm hover:bg-muted"
              >
                <input
                  type="radio"
                  name={name}
                  checked={selected === edition.externalId}
                  onChange={() => setChosen(edition.externalId)}
                />
                <span className="min-w-0 flex-1">{editionLabel(edition)}</span>
              </label>
            ))}
            {data.truncated && (
              <p className="px-2 pt-1 text-xs text-muted-foreground">
                The provider has more editions than are listed.
              </p>
            )}
          </fieldset>
          {selected === null ? (
            <p className="px-2 text-sm text-muted-foreground">
              Choose an edition to see its tracks.
            </p>
          ) : (
            <EditionTrackList key={selected} editionId={selected} album={data.album} />
          )}
        </>
      )}
    </div>
  )
}
