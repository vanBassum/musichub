using System.Text.Json;
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

    /// <summary>The identity could not be established. Nothing is wanted yet; a review case is waiting.</summary>
    public const string Unresolved = "unresolved";
}

/// <param name="Status">One of <see cref="WishlistStatus"/>.</param>
/// <param name="Track">The catalog track, for <see cref="WishlistStatus.Added"/> and <see cref="WishlistStatus.AlreadyWanted"/>.</param>
/// <param name="Created">The catalog track was created by this call.</param>
/// <param name="ReviewId">The pending identity review, for <see cref="WishlistStatus.Unresolved"/>.</param>
/// <param name="CandidateTrackIds">The catalog tracks that resemble the external track without proof, for <see cref="WishlistStatus.Unresolved"/>.</param>
public record WishlistResponse(
    string Status,
    TrackResponse? Track,
    bool Created,
    int? ReviewId,
    List<int> CandidateTrackIds);

/// <summary>
/// Marks catalog tracks wanted, idempotently. An external track is resolved on reliable evidence only: a provider
/// identifier already mapped to a catalog track reuses that track. With no such mapping, a catalog track with the same
/// title and a shared artist is a candidate, but title and artist similarity never proves identity, so the external
/// track is not merged and not created: a pending <see cref="IdentityReview"/> is kept and nothing becomes wanted. Only
/// when no catalog track resembles it is a new track created (with the provider identifier as its evidence) and wanted.
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

            var review = await db.IdentityReviews.FirstOrDefaultAsync(
                r => r.Provider == provider && r.ExternalId == externalId, cancellationToken);
            if (review is not null)
                return Unresolved(review);

            var candidates = await FindCandidatesAsync(title, artists, cancellationToken);
            if (candidates.Count > 0)
            {
                review = new IdentityReview
                {
                    Provider = provider,
                    ExternalId = externalId,
                    Title = title,
                    Disambiguation = version,
                    ArtistsJson = JsonSerializer.Serialize(artists),
                    CandidateTrackIdsJson = JsonSerializer.Serialize(candidates),
                    CreatedAt = time.GetUtcNow(),
                };
                db.IdentityReviews.Add(review);
                await db.SaveChangesAsync(cancellationToken);
                return Unresolved(review);
            }

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
        return new WishlistResponse(status, response, created, null, []);
    }

    private static WishlistResponse Unresolved(IdentityReview review) => new(
        WishlistStatus.Unresolved, null, false, review.Id,
        JsonSerializer.Deserialize<List<int>>(review.CandidateTrackIdsJson) ?? []);

    // Same title, ignoring case, and at least one artist in common. The version is deliberately not compared: a
    // differing version is not proof of a distinct recording either.
    private async Task<List<int>> FindCandidatesAsync(
        string title, List<string> artists, CancellationToken cancellationToken)
    {
        var lower = title.ToLower();
        var sameTitle = await db.Tracks.AsNoTracking()
            .Include(t => t.Artists).ThenInclude(a => a.Artist)
            .Where(t => t.Title.ToLower() == lower)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);

        var wanted = artists.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return sameTitle
            .Where(t => t.Artists.Any(a => wanted.Contains(a.Artist.Name.Trim())))
            .Select(t => t.Id)
            .ToList();
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
