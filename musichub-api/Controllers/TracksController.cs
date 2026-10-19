using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Controllers;

/// <summary>
/// The catalog of known tracks, independent of audio files. It lists, searches and shows tracks and lets the
/// user mark them wanted (the wishlist); other changes to the catalog are not part of this API.
/// </summary>
[ApiController]
[Route("api/tracks")]
public class TracksController(MusicHubDbContext db, Wishlist wishlist) : ControllerBase
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

    /// <summary>Marks an existing catalog track wanted. Idempotent: 200 with status added or alreadyWanted.</summary>
    [HttpPut("{id:int}/wanted")]
    public async Task<IActionResult> Want(int id, CancellationToken cancellationToken)
    {
        var result = await wishlist.AddCatalogTrackAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    /// <summary>
    /// Wants a track the user chose from external search results. Idempotent. Answers 201 when the catalog track was
    /// created, 200 when an existing track was reused (status added or alreadyWanted), and 202 with status unresolved
    /// when the identity is not established: nothing is wanted then, and a pending identity review holds the case.
    /// </summary>
    [HttpPost("wishlist/external")]
    public async Task<IActionResult> WantExternal(
        [FromBody] ExternalTrackWishlistRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Provider)) errors["provider"] = ["Provider is required."];
        if (string.IsNullOrWhiteSpace(request.ExternalId)) errors["externalId"] = ["External id is required."];
        if (string.IsNullOrWhiteSpace(request.Title)) errors["title"] = ["Title is required."];
        if (request.Artists is null || request.Artists.Any(a => a is null))
            errors["artists"] = ["Artists must be a list of artists with a name."];
        else if (!request.Artists.Any(a => !string.IsNullOrWhiteSpace(a.Name)))
            errors["artists"] = ["At least one artist with a name is required."];
        else if (request.Artists.Any(a => a.Name?.Trim().Length > MaxArtistNameLength))
            errors["artists"] = [$"An artist name is at most {MaxArtistNameLength} characters."];
        if (TooLong(request.Provider, MaxProviderLength)) errors["provider"] = [$"Provider is at most {MaxProviderLength} characters."];
        if (TooLong(request.ExternalId, MaxExternalIdLength)) errors["externalId"] = [$"External id is at most {MaxExternalIdLength} characters."];
        if (TooLong(request.Title, MaxTitleLength)) errors["title"] = [$"Title is at most {MaxTitleLength} characters."];
        if (TooLong(request.Disambiguation, MaxTitleLength)) errors["disambiguation"] = [$"Disambiguation is at most {MaxTitleLength} characters."];
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors));

        var result = await wishlist.AddExternalTrackAsync(request, cancellationToken);
        return result.Status == WishlistStatus.Unresolved
            ? StatusCode(StatusCodes.Status202Accepted, result)
            : result.Created
                ? StatusCode(StatusCodes.Status201Created, result)
                : Ok(result);
    }

    // The column limits of the catalog tables (MusicHubDbContext), checked on the trimmed value that is stored.
    private const int MaxProviderLength = 100;
    private const int MaxExternalIdLength = 200;
    private const int MaxTitleLength = 500;
    private const int MaxArtistNameLength = 500;

    private static bool TooLong(string? value, int max) => value?.Trim().Length > max;

    private Task<TrackResponse?> FindResponse(int id, CancellationToken cancellationToken) =>
        db.Tracks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(TrackResponse.Projection)
            .SingleOrDefaultAsync(cancellationToken);
}
