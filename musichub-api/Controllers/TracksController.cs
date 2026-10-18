using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Controllers;

/// <summary>
/// The catalog of known tracks, independent of audio files. Registers and edits track
/// metadata and its artists; uploading, importing and identifying audio live elsewhere.
/// </summary>
[ApiController]
[Route("api/tracks")]
public class TracksController(MusicHubDbContext db) : ControllerBase
{
    public const int MaxTitleLength = 500;
    public const int MaxNameLength = 500;
    public const int MaxRoleLength = 100;
    public const int MaxPageSize = 100;
    private const int DefaultPageSize = 25;

    /// <summary>Lists tracks by title, a page at a time.</summary>
    /// <param name="search">Case-insensitive text matched against title, version and artist name.</param>
    /// <param name="hasAudio">Only tracks that do (true) or do not (false) have stored audio linked.</param>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] bool? hasAudio,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["Page must be 1 or greater."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors));

        var query = db.Tracks.AsNoTracking();

        var term = search?.Trim().ToLower();
        if (!string.IsNullOrEmpty(term))
        {
            query = query.Where(t =>
                t.Title.ToLower().Contains(term)
                || (t.Version != null && t.Version.ToLower().Contains(term))
                || t.Artists.Any(a => a.Artist.Name.ToLower().Contains(term)));
        }

        if (hasAudio is { } wanted)
            query = query.Where(t => t.Files.Any(f => f.Status == FileStatus.Stored) == wanted);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(t => t.Title).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(TrackResponse.Projection)
            .ToListAsync(cancellationToken);

        return Ok(new PagedResponse<TrackResponse>(items, total, page, pageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var track = await FindResponse(id, cancellationToken);
        return track is null ? NotFound() : Ok(track);
    }

    /// <summary>Registers a track; no audio file is needed.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TrackRequest request, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors));

        var track = new Track
        {
            Title = request.Title!.Trim(),
            Version = NullIfBlank(request.Version),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var links = await ResolveArtists(request.Artists ?? [], errors, cancellationToken);
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors));

        foreach (var (artist, role) in links)
            track.Artists.Add(new TrackArtist { Artist = artist, Role = role });

        db.Tracks.Add(track);
        await db.SaveChangesAsync(cancellationToken);

        var response = await FindResponse(track.Id, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = track.Id }, response);
    }

    /// <summary>Replaces the track's title, version and artists with the request's.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TrackRequest request, CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors));

        var track = await db.Tracks.Include(t => t.Artists).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (track is null)
            return NotFound();

        var links = await ResolveArtists(request.Artists ?? [], errors, cancellationToken);
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors));

        track.Title = request.Title!.Trim();
        track.Version = NullIfBlank(request.Version);

        // Diff against the existing links so a kept link isn't deleted and re-inserted
        // (that would collide with the unique index within one save).
        var wanted = links.ToList();
        foreach (var existing in track.Artists.ToList())
        {
            var match = wanted.FindIndex(l => l.Artist.Id == existing.ArtistId && l.Role == existing.Role);
            if (match >= 0)
                wanted.RemoveAt(match);
            else
                db.TrackArtists.Remove(existing);
        }

        foreach (var (artist, role) in wanted)
            track.Artists.Add(new TrackArtist { Artist = artist, Role = role });

        await db.SaveChangesAsync(cancellationToken);

        return Ok(await FindResponse(id, cancellationToken));
    }

    private Task<TrackResponse?> FindResponse(int id, CancellationToken cancellationToken) =>
        db.Tracks.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(TrackResponse.Projection)
            .SingleOrDefaultAsync(cancellationToken);

    private static Dictionary<string, string[]> Validate(TrackRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["request"] = ["A request body is required."];
            return errors;
        }

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
            errors["title"] = ["Title is required."];
        else if (title.Length > MaxTitleLength)
            errors["title"] = [$"Title must be at most {MaxTitleLength} characters."];

        if (request.Version?.Trim().Length > MaxTitleLength)
            errors["version"] = [$"Version must be at most {MaxTitleLength} characters."];

        var artists = request.Artists ?? [];
        for (var i = 0; i < artists.Count; i++)
        {
            var key = $"artists[{i}]";
            var artist = artists[i];
            if (artist is null)
            {
                errors[key] = ["Artist must not be null."];
                continue;
            }

            var name = artist.Name?.Trim();
            if (artist.ArtistId.HasValue == !string.IsNullOrEmpty(name))
                errors[key] = ["Give either artistId or name."];
            else if (name?.Length > MaxNameLength)
                errors[$"{key}.name"] = [$"Name must be at most {MaxNameLength} characters."];

            if (artist.Role?.Trim().Length > MaxRoleLength)
                errors[$"{key}.role"] = [$"Role must be at most {MaxRoleLength} characters."];
        }

        return errors;
    }

    /// <summary>
    /// Resolves the requested artists to entities (new ones are not saved until the caller saves)
    /// and rejects the same artist twice in the same role.
    /// </summary>
    private async Task<List<(Artist Artist, string? Role)>> ResolveArtists(
        List<TrackArtistRequest> requested, Dictionary<string, string[]> errors, CancellationToken cancellationToken)
    {
        var resolved = new List<(Artist Artist, string? Role)>();
        var byName = new Dictionary<string, Artist>();

        for (var i = 0; i < requested.Count; i++)
        {
            var item = requested[i];
            var role = NullIfBlank(item.Role);
            Artist? artist;

            if (item.ArtistId is { } artistId)
            {
                artist = await db.Artists.FindAsync([artistId], cancellationToken);
                if (artist is null)
                {
                    errors[$"artists[{i}].artistId"] = [$"Artist {artistId} does not exist."];
                    continue;
                }
            }
            else
            {
                var name = item.Name!.Trim();
                var lowered = name.ToLower();
                if (!byName.TryGetValue(lowered, out artist))
                {
                    artist = await db.Artists.FirstOrDefaultAsync(a => a.Name.ToLower() == lowered, cancellationToken)
                        ?? new Artist { Name = name, CreatedAt = DateTimeOffset.UtcNow };
                    byName[lowered] = artist;
                }
            }

            if (resolved.Any(r => ReferenceEquals(r.Artist, artist) && r.Role == role))
            {
                errors[$"artists[{i}]"] = ["The same artist with the same role is listed twice."];
                continue;
            }

            resolved.Add((artist, role));
        }

        return resolved;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
