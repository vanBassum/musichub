using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.ExternalSearch;

namespace MusicHub.Api.Controllers;

public record ExternalTrackListResponse(string Provider, IReadOnlyList<ExternalTrack> Items);

public record ExternalArtistListResponse(string Provider, IReadOnlyList<ExternalArtist> Items);

/// <summary>
/// Searches the external metadata provider. Read-only: results are not catalog records, nothing is stored and nothing
/// is cached. A missing, unreachable or rate-limited provider answers 503.
/// </summary>
[ApiController]
[Route("api/external")]
public class ExternalSearchController(IExternalMusicSearch? provider = null) : ControllerBase
{
    public const int MaxLimit = 20;
    private const int DefaultLimit = 5;

    /// <summary>Searches tracks by title and/or artist name.</summary>
    /// <param name="search">The text to search for.</param>
    /// <param name="fields">Comma-separated: <c>title</c>, <c>artist</c>. Defaults to both.</param>
    [HttpGet("tracks")]
    public async Task<IActionResult> Tracks(
        [FromQuery] string? search,
        [FromQuery] string? fields,
        [FromQuery] int limit = DefaultLimit,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (Validate(search, limit, offset) is { } invalid)
        {
            return invalid;
        }
        var parsed = ParseFields(fields);
        if (parsed is null)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["fields"] = ["Fields must be a comma-separated list of: title, artist."],
            }));
        }

        return await Query(
            p => p.SearchTracksAsync(search!.Trim(), parsed, limit, offset, cancellationToken),
            items => new ExternalTrackListResponse(provider!.Provider, items));
    }

    /// <summary>Searches artists by name.</summary>
    [HttpGet("artists")]
    public async Task<IActionResult> Artists(
        [FromQuery] string? search,
        [FromQuery] int limit = DefaultLimit,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (Validate(search, limit, offset) is { } invalid)
        {
            return invalid;
        }

        return await Query(
            p => p.SearchArtistsAsync(search!.Trim(), limit, offset, cancellationToken),
            items => new ExternalArtistListResponse(provider!.Provider, items));
    }

    private Task<IActionResult> Query<T>(
        Func<IExternalMusicSearch, Task<IReadOnlyList<T>>> search, Func<IReadOnlyList<T>, object> respond) =>
        this.WithProvider(provider, async p => Ok(respond(await search(p))));

    private IActionResult? Validate(string? search, int limit, int offset)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(search))
        {
            errors["search"] = ["Search text is required."];
        }
        if (limit is < 1 or > MaxLimit)
        {
            errors["limit"] = [$"Limit must be between 1 and {MaxLimit}."];
        }
        return errors.Count == 0 ? this.InvalidOffset(offset) : BadRequest(new ValidationProblemDetails(errors));
    }

    private static ExternalSearchFields? ParseFields(string? fields)
    {
        if (string.IsNullOrWhiteSpace(fields))
        {
            return new ExternalSearchFields(true, true);
        }
        bool title = false, artist = false;
        foreach (var name in fields.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (name.ToLowerInvariant())
            {
                case "title": title = true; break;
                case "artist": artist = true; break;
                default: return null;
            }
        }
        return title || artist ? new ExternalSearchFields(title, artist) : null;
    }
}
