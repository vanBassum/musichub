using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Controllers;

/// <summary>The artists of the catalog. Read-only: it only lists and searches them.</summary>
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
}
