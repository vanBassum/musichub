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
public record ExternalArtistTrack(ExternalTrack Track, int? CatalogTrackId, bool? HasAudio);

public record ExternalArtistTrackListResponse(string Provider, IReadOnlyList<ExternalArtistTrack> Items);

/// <summary>
/// One artist of the external metadata provider, and the recordings it credits to them. Read-only: nothing is
/// registered in the catalog and nothing is cached. A missing, unreachable or rate-limited provider answers 503.
/// </summary>
[ApiController]
[Route("api/external/artists/{externalId}")]
public class ExternalArtistsController(MusicHubDbContext db, IExternalMusicSearch? provider = null) : ControllerBase
{
    public const int MaxLimit = 100;
    private const int DefaultLimit = 25;

    [HttpGet]
    public async Task<IActionResult> Get(string externalId, CancellationToken cancellationToken)
    {
        if (Check(externalId) is { } invalid)
            return invalid;
        try
        {
            var artist = await provider!.GetArtistAsync(externalId, cancellationToken);
            return artist is null ? NotFound() : Ok(new ExternalArtistResponse(provider.Provider, artist));
        }
        catch (CandidateSearchUnavailableException e)
        {
            return Unavailable(e.Message);
        }
    }

    /// <summary>The artist's recordings as the provider lists them (no ranking), each marked with the catalog track it already is.</summary>
    [HttpGet("tracks")]
    public async Task<IActionResult> Tracks(
        string externalId, [FromQuery] int limit = DefaultLimit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxLimit)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["limit"] = [$"Limit must be between 1 and {MaxLimit}."],
            }));
        }
        if (Check(externalId) is { } invalid)
            return invalid;

        try
        {
            var tracks = await provider!.GetArtistTracksAsync(externalId, limit, cancellationToken);
            var ids = tracks.Select(t => t.ExternalId).ToList();
            var known = await db.TrackExternalIds.AsNoTracking()
                .Where(e => e.Provider == provider.Provider && ids.Contains(e.ExternalId))
                .Select(e => new
                {
                    e.ExternalId,
                    e.TrackId,
                    HasAudio = e.Track.Files.AsQueryable().Any(TrackResponse.IsStoredAudio),
                })
                .ToListAsync(cancellationToken);
            var byId = known.ToDictionary(k => k.ExternalId);

            var items = tracks
                .Select(t => byId.TryGetValue(t.ExternalId, out var k)
                    ? new ExternalArtistTrack(t, k.TrackId, k.HasAudio)
                    : new ExternalArtistTrack(t, null, null))
                .ToList();
            return Ok(new ExternalArtistTrackListResponse(provider.Provider, items));
        }
        catch (CandidateSearchUnavailableException e)
        {
            return Unavailable(e.Message);
        }
    }

    // Null when the provider is configured and the identifier is one of its valid ones.
    private IActionResult? Check(string externalId)
    {
        if (provider is null)
            return Unavailable("External search is not configured.");
        if (ExternalSources.Normalize(provider.Provider, externalId) is not { } normalized
            || normalized.ExternalId != externalId)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["externalId"] = [$"Not a valid {provider.Provider} artist identifier."],
            }));
        }
        return null;
    }

    private ObjectResult Unavailable(string detail) =>
        Problem(detail, statusCode: StatusCodes.Status503ServiceUnavailable, title: "External search is unavailable.");
}
