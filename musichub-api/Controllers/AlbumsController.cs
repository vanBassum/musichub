using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Controllers;

/// <summary>
/// Published albums and compilations: their editions, each a fixed ordered list of catalog tracks, whether or not
/// any audio is available. Read only: albums are not edited through this API.
/// </summary>
[ApiController]
[Route("api/albums")]
public class AlbumsController(MusicHubDbContext db) : ControllerBase
{
    private const int DefaultPageSize = 25;

    /// <summary>Lists albums by title, a page at a time.</summary>
    /// <param name="search">Case-insensitive text matched against the album title.</param>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["Page must be 1 or greater."];
        if (pageSize is < 1 or > TracksController.MaxPageSize)
            errors["pageSize"] = [$"Page size must be between 1 and {TracksController.MaxPageSize}."];
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors));

        var query = AlbumQueries.Filter(db.Albums.AsNoTracking(), search);
        var total = await query.CountAsync(cancellationToken);
        var items = await AlbumQueries.Page(query, page, pageSize).ToListAsync(cancellationToken);
        return Ok(new PagedResponse<AlbumListItemResponse>(items, total, page, pageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var album = await AlbumQueries.FindAsync(db, id, cancellationToken);
        return album is null ? NotFound() : Ok(album);
    }
}
