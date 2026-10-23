using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Controllers;

/// <summary>
/// The library: the catalog of known tracks, independent of audio files. It lists, searches and shows tracks (with
/// hasAudio=false for missing music) and adds external tracks to it; other changes to the catalog are not part of
/// this API.
/// </summary>
[ApiController]
[Route("api/tracks")]
public class TracksController(MusicHubDbContext db, LibraryTracks library) : ControllerBase
{
    public const int MaxPageSize = 100;
    private const int DefaultPageSize = 25;

    /// <summary>Lists tracks by title, a page at a time.</summary>
    /// <param name="search">Case-insensitive text matched against title, version and artist name.</param>
    /// <param name="fields">Comma-separated fields the search looks in: title (title and version), artist. Default: both.</param>
    /// <param name="hasAudio">Only tracks that do (true: available music) or do not (false: missing music) have stored audio linked.</param>
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
        Paging.Validate(errors, page, pageSize, MaxPageSize);
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

    /// <summary>
    /// Adds a track the user chose from external search results to the library. Idempotent. Answers 201 when the
    /// catalog track was created (no catalog track is mapped to the provider identifier), and 200 when the mapped
    /// track was reused. Nothing is downloaded.
    /// </summary>
    [HttpPost("external")]
    public async Task<IActionResult> AddExternal(
        [FromBody] ExternalTrackRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Provider)) errors["provider"] = ["Provider is required."];
        if (string.IsNullOrWhiteSpace(request.ExternalId)) errors["externalId"] = ["External id is required."];
        if (string.IsNullOrWhiteSpace(request.Title)) errors["title"] = ["Title is required."];
        if (request.Artists is null || request.Artists.Any(a => a is null))
            errors["artists"] = ["Artists must be a list of artists with a name."];
        else if (!request.Artists.Any(a => !string.IsNullOrWhiteSpace(a.Name)))
            errors["artists"] = ["At least one artist with a name is required."];
        else if (request.Artists.Any(a => a.Name?.Trim().Length > CatalogLimits.ArtistName))
            errors["artists"] = [$"An artist name is at most {CatalogLimits.ArtistName} characters."];
        if (TooLong(request.Provider, CatalogLimits.Provider)) errors["provider"] = [$"Provider is at most {CatalogLimits.Provider} characters."];
        if (TooLong(request.ExternalId, CatalogLimits.ExternalId)) errors["externalId"] = [$"External id is at most {CatalogLimits.ExternalId} characters."];
        if (TooLong(request.Title, CatalogLimits.Title)) errors["title"] = [$"Title is at most {CatalogLimits.Title} characters."];
        if (TooLong(request.Disambiguation, CatalogLimits.Version)) errors["disambiguation"] = [$"Disambiguation is at most {CatalogLimits.Version} characters."];
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors));

        var result = await library.AddExternalTrackAsync(request, cancellationToken);
        return result.Created ? StatusCode(StatusCodes.Status201Created, result) : Ok(result);
    }

    // Checked on the trimmed value, which is what is stored.
    private static bool TooLong(string? value, int max) => value?.Trim().Length > max;

    private Task<TrackResponse?> FindResponse(int id, CancellationToken cancellationToken) =>
        db.Tracks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(TrackResponse.Projection)
            .SingleOrDefaultAsync(cancellationToken);
}
