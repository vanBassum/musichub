using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.ExternalSearch;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Controllers;

public record ExternalArtistResponse(string Provider, ExternalArtist Artist);

/// <param name="CatalogTrackId">The catalog track this recording is, by the provider's own identifier; null when it is not in the catalog.</param>
/// <param name="HasAudio">Whether that catalog track has stored audio; null when it is not in the catalog.</param>
/// <param name="Popularity">The popularity source's own listen counts for this recording; null when it has none. Never a MusicHub score.</param>
public record ExternalArtistTrack(
    ExternalTrack Track, int? CatalogTrackId, bool? HasAudio, ExternalTrackPopularity? Popularity = null);

/// <summary>What the optional popularity source answered, kept apart from the tracks so a failure is not read as "no data".</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PopularityAvailability
{
    /// <summary>The source answered with popularity for the artist.</summary>
    Provided,

    /// <summary>The source answered and has no popularity for the artist.</summary>
    NothingFound,

    /// <summary>The source could not answer (network, timeout, rate limit, service error).</summary>
    Unavailable,
}

public record ExternalPopularityStatus(string Provider, PopularityAvailability Availability);

/// <param name="Popularity">Null when no popularity source is configured.</param>
public record ExternalArtistTrackListResponse(
    string Provider, IReadOnlyList<ExternalArtistTrack> Items, ExternalPopularityStatus? Popularity = null);

/// <summary>
/// One artist of the external metadata provider, and the recordings it credits to them. Read-only: nothing is
/// registered in the catalog and nothing is cached. A missing, unreachable or rate-limited provider answers 503.
/// </summary>
[ApiController]
[Route("api/external/artists/{externalId}")]
public class ExternalArtistsController(
    MusicHubDbContext db,
    IExternalMusicSearch? provider = null,
    IExternalPopularitySource? popularitySource = null) : ControllerBase
{
    [HttpGet]
    public Task<IActionResult> Get(string externalId, CancellationToken cancellationToken) =>
        this.WithProvider(provider, async p =>
        {
            if (Check(p, externalId) is { } invalid)
                return invalid;
            var artist = await p.GetArtistAsync(externalId, cancellationToken);
            return artist is null ? NotFound() : Ok(new ExternalArtistResponse(p.Provider, artist));
        });

    /// <summary>The artist's recordings as the provider lists them (no ranking), each marked with the catalog track it already is.</summary>
    [HttpGet("tracks")]
    public async Task<IActionResult> Tracks(
        string externalId, [FromQuery] int limit = ExternalResults.DefaultListLimit, CancellationToken cancellationToken = default)
    {
        if (this.InvalidLimit(limit) is { } invalidLimit)
            return invalidLimit;

        return await this.WithProvider(provider, async p =>
        {
            if (Check(p, externalId) is { } invalid)
                return invalid;
            var tracks = await p.GetArtistTracksAsync(externalId, limit, cancellationToken);
            var ids = tracks.Select(t => t.ExternalId).ToList();
            var known = await db.TrackExternalIds.AsNoTracking()
                .Where(e => e.Provider == p.Provider && ids.Contains(e.ExternalId))
                .Select(e => new
                {
                    e.ExternalId,
                    e.TrackId,
                    HasAudio = e.Track.Files.AsQueryable().Any(TrackResponse.IsStoredAudio),
                })
                .ToListAsync(cancellationToken);
            var byId = known.ToDictionary(k => k.ExternalId);

            var (popularity, status) = await ReadPopularityAsync(externalId, cancellationToken);
            var items = tracks
                .Select(t =>
                {
                    popularity.TryGetValue(t.ExternalId, out var listens);
                    return byId.TryGetValue(t.ExternalId, out var k)
                        ? new ExternalArtistTrack(t, k.TrackId, k.HasAudio, listens)
                        : new ExternalArtistTrack(t, null, null, listens);
                })
                .ToList();
            return Ok(new ExternalArtistTrackListResponse(p.Provider, items, status));
        });
    }

    // Optional enrichment: its failure is reported in the status and never fails the track list.
    private async Task<(Dictionary<string, ExternalTrackPopularity> ByRecording, ExternalPopularityStatus? Status)>
        ReadPopularityAsync(string externalId, CancellationToken cancellationToken)
    {
        if (popularitySource is null)
            return ([], null);
        try
        {
            var found = await popularitySource.GetArtistPopularityAsync(externalId, cancellationToken);
            var availability = found.Count == 0 ? PopularityAvailability.NothingFound : PopularityAvailability.Provided;
            return (found.ToDictionary(p => p.RecordingId), new ExternalPopularityStatus(popularitySource.Provider, availability));
        }
        catch (CandidateSearchUnavailableException)
        {
            return ([], new ExternalPopularityStatus(popularitySource.Provider, PopularityAvailability.Unavailable));
        }
    }

    // Null when the identifier is one of the provider's valid ones.
    private IActionResult? Check(IExternalMusicSearch p, string externalId) =>
        this.InvalidId(p.Provider, externalId, "artist");
}
