using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;

namespace MusicHub.Api.Catalog;

public record ExternalArtistCreditRequest(string? Name);

/// <summary>An external track the user chose to add to the library: the provider's identity and display text.</summary>
public record ExternalTrackRequest(
    string? Provider,
    string? ExternalId,
    string? Title,
    string? Disambiguation,
    List<ExternalArtistCreditRequest>? Artists);

/// <param name="Track">The catalog track the library holds for the external track.</param>
/// <param name="Created">The catalog track was created by this call.</param>
public record AddToLibraryResponse(TrackResponse Track, bool Created);

/// <summary>
/// Adds an external track the user chose to the library, idempotently. It reuses the catalog track its provider
/// identifier is already mapped to. Otherwise a new track is created, with the provider identifier as its evidence.
/// A resembling title and artist is never proof of identity, so nothing is merged, and it does not block the add
/// either: an occasional duplicate catalog track is accepted, and cleanup is deferred. Adding never starts a
/// download: a track without an available file is listed as missing music.
/// </summary>
public sealed class LibraryTracks(MusicHubDbContext db, TimeProvider time)
{
    public async Task<AddToLibraryResponse> AddExternalTrackAsync(
        ExternalTrackRequest request, CancellationToken cancellationToken)
    {
        var provider = request.Provider!.Trim();
        var externalId = request.ExternalId!.Trim();
        var title = request.Title!.Trim();
        var version = string.IsNullOrWhiteSpace(request.Disambiguation) ? null : request.Disambiguation.Trim();
        var artists = CatalogArtists.Normalize(request.Artists!.Select(a => a?.Name));

        await CatalogReconciliation.CatalogLock.WaitAsync(cancellationToken);
        try
        {
            var known = await db.TrackExternalIds
                .Where(e => e.Provider == provider && e.ExternalId == externalId)
                .Select(e => e.Track)
                .FirstOrDefaultAsync(cancellationToken);
            if (known is not null)
                return new AddToLibraryResponse(await FindResponseAsync(known.Id, cancellationToken), Created: false);

            var track = await CreateTrackAsync(provider, externalId, title, version, artists, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new AddToLibraryResponse(await FindResponseAsync(track.Id, cancellationToken), Created: true);
        }
        finally
        {
            CatalogReconciliation.CatalogLock.Release();
        }
    }

    private Task<TrackResponse> FindResponseAsync(int trackId, CancellationToken cancellationToken) =>
        db.Tracks.AsNoTracking()
            .Where(t => t.Id == trackId)
            .Select(TrackResponse.Projection)
            .SingleAsync(cancellationToken);

    private async Task<Track> CreateTrackAsync(
        string provider, string externalId, string title, string? version, List<string> artists,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var track = new Track { Title = title, Version = version, CreatedAt = now };
        await CatalogArtists.AttachAsync(db, track, artists, now, cancellationToken);
        track.ExternalIds.Add(new TrackExternalId { Provider = provider, ExternalId = externalId });
        db.Tracks.Add(track);
        return track;
    }
}
