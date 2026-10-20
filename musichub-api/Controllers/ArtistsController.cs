using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Controllers;

/// <summary>The artists of the catalog. Read-only: it only lists, searches and reads them.</summary>
[ApiController]
[Route("api/artists")]
public class ArtistsController(MusicHubDbContext db) : ControllerBase
{
    public const int MaxPageSize = 50;
    private const int DefaultPageSize = 10;

    /// <summary>Lists artists by name, with the number of tracks each has.</summary>
    /// <param name="search">Case-insensitive text matched against the artist name.</param>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > MaxPageSize)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."],
            }));
        }

        var query = ArtistListQuery.Filter(db.Artists.AsNoTracking(), search);
        var total = await query.CountAsync(cancellationToken);
        var items = await ArtistListQuery.Page(query, pageSize).ToListAsync(cancellationToken);

        return Ok(new ArtistListResponse(items, total));
    }

    /// <summary>One artist, with the external sources linked to it.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var artist = await db.Artists.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.Name,
                TrackCount = a.Tracks.Select(l => l.TrackId).Distinct().Count(),
                Links = a.ExternalLinks
                    .Where(l => l.Status == ExternalLinkStatus.Linked)
                    .OrderBy(l => l.Source).ThenBy(l => l.Id)
                    .Select(l => new { l.Source, l.ExternalId })
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (artist is null)
            return NotFound();

        var sources = artist.Links
            .Select(l => new LinkedArtistSource(l.Source, l.ExternalId, ExternalSources.ArtistUrl(l.Source, l.ExternalId)))
            .ToList();
        return Ok(new ArtistDetailResponse(artist.Id, artist.Name, artist.TrackCount, sources));
    }

    /// <summary>The catalog tracks of an artist by title.</summary>
    [HttpGet("{id:int}/tracks")]
    public async Task<IActionResult> Tracks(
        int id, [FromQuery] int pageSize = MaxPageSize, CancellationToken cancellationToken = default)
    {
        if (pageSize is < 1 or > MaxPageSize)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."],
            }));
        }
        if (!await db.Artists.AnyAsync(a => a.Id == id, cancellationToken))
            return NotFound();

        var tracks = db.Tracks.AsNoTracking().Where(t => t.Artists.Any(a => a.ArtistId == id));
        var total = await tracks.CountAsync(cancellationToken);
        var items = await tracks
            .OrderBy(t => t.Title).ThenBy(t => t.Id)
            .Take(pageSize)
            .Select(TrackResponse.Projection)
            .ToListAsync(cancellationToken);
        return Ok(new ArtistTrackListResponse(items, total));
    }

    /// <summary>
    /// The catalog artist an external identifier is linked to; 404 when it is linked to none. It only follows
    /// grounded links, never a name, and registers nothing.
    /// </summary>
    [HttpGet("by-external/{source}/{externalId}")]
    public async Task<IActionResult> ByExternal(
        string source, string externalId, CancellationToken cancellationToken)
    {
        var artist = await new ArtistExternalLinks(db, TimeProvider.System)
            .FindLinkedArtistAsync(new ArtistReference(source, externalId, ""), cancellationToken);
        return artist is null ? NotFound() : Ok(new LinkedArtistResponse(artist.Id));
    }
}
