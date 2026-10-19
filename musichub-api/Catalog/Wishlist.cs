using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;

namespace MusicHub.Api.Catalog;

public record ExternalArtistCreditRequest(string? Name);

/// <summary>An external track the user chose to want: the provider's identity and display text.</summary>
public record ExternalTrackWishlistRequest(
    string? Provider,
    string? ExternalId,
    string? Title,
    string? Disambiguation,
    List<ExternalArtistCreditRequest>? Artists);

public static class WishlistStatus
{
    /// <summary>The track is now wanted; it was not before.</summary>
    public const string Added = "added";

    /// <summary>The track was already wanted; nothing changed.</summary>
    public const string AlreadyWanted = "alreadyWanted";
}

/// <param name="Status">One of <see cref="WishlistStatus"/>.</param>
/// <param name="Track">The catalog track that is wanted.</param>
/// <param name="Created">The catalog track was created by this call.</param>
public record WishlistResponse(string Status, TrackResponse Track, bool Created);

/// <summary>
/// Marks catalog tracks wanted, idempotently. An external track the user chose reuses the catalog track its provider
/// identifier is already mapped to. Otherwise a new track is created, with the provider identifier as its evidence,
/// and wanted. A resembling title and artist is never proof of identity, so nothing is merged, and it does not block
/// the add either: an occasional duplicate catalog track is accepted, and cleanup is deferred.
/// </summary>
public sealed class Wishlist(MusicHubDbContext db, TimeProvider time)
{
    public async Task<WishlistResponse?> AddCatalogTrackAsync(int trackId, CancellationToken cancellationToken)
    {
        var track = await db.Tracks.FirstOrDefaultAsync(t => t.Id == trackId, cancellationToken);
        return track is null ? null : await WantAsync(track, created: false, cancellationToken);
    }

    public async Task<WishlistResponse> AddExternalTrackAsync(
        ExternalTrackWishlistRequest request, CancellationToken cancellationToken)
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
                return await WantAsync(known, created: false, cancellationToken);

            var track = await CreateTrackAsync(provider, externalId, title, version, artists, cancellationToken);
            return await WantAsync(track, created: true, cancellationToken);
        }
        finally
        {
            CatalogReconciliation.CatalogLock.Release();
        }
    }

    private async Task<WishlistResponse> WantAsync(Track track, bool created, CancellationToken cancellationToken)
    {
        var status = WishlistStatus.AlreadyWanted;
        // Wanting is idempotent: only the first call sets the time.
        if (track.WantedAt is null)
        {
            track.WantedAt = time.GetUtcNow();
            status = WishlistStatus.Added;
        }
        await db.SaveChangesAsync(cancellationToken);

        var response = await db.Tracks.AsNoTracking()
            .Where(t => t.Id == track.Id)
            .Select(TrackResponse.Projection)
            .SingleAsync(cancellationToken);
        return new WishlistResponse(status, response, created);
    }

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
