using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Controllers;

/// <summary>
/// The catalog of known tracks, independent of audio files. Read-only: it lists, searches
/// and shows tracks; how tracks enter or change in the catalog is not part of this API.
/// </summary>
[ApiController]
[Route("api/tracks")]
public class TracksController(MusicHubDbContext db) : ControllerBase
{
    public const int MaxPageSize = 100;
    private const int DefaultPageSize = 25;

    /// <summary>Lists tracks by title, a page at a time.</summary>
    /// <param name="search">Case-insensitive text matched against title, version and artist name.</param>
    /// <param name="fields">Comma-separated fields the search looks in: title (title and version), artist. Default: both.</param>
    /// <param name="hasAudio">Only tracks that do (true) or do not (false) have stored audio linked.</param>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool? hasAudio,
        [FromQuery] string? fields,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["Page must be 1 or greater."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];
        var searchFields = TrackListQuery.ParseFields(fields);
        if (searchFields is null) errors["fields"] = ["Fields must be a comma-separated list of: title, artist."];
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors));

        var query = TrackListQuery.Filter(db.Tracks.AsNoTracking(), search, hasAudio, searchFields);

        var total = await query.CountAsync(cancellationToken);
        var items = await TrackListQuery.Page(query, page, pageSize).ToListAsync(cancellationToken);

        return Ok(new PagedResponse<TrackResponse>(items, total, page, pageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var track = await FindResponse(id, cancellationToken);
        return track is null ? NotFound() : Ok(track);
    }

    private Task<TrackResponse?> FindResponse(int id, CancellationToken cancellationToken) =>
        db.Tracks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(TrackResponse.Projection)
            .SingleOrDefaultAsync(cancellationToken);
}
