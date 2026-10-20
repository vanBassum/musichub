using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Controllers;

/// <summary>An artist's external identifiers: inspect them, link the right one and unlink a wrong one.</summary>
[ApiController]
[Route("api/artists/{artistId:int}/external-links")]
public class ArtistExternalLinksController(MusicHubDbContext db, TimeProvider time) : ControllerBase
{
    private readonly ArtistExternalLinks links = new(db, time);

    /// <summary>Lists the artist's links, including the ones a person unlinked.</summary>
    [HttpGet]
    public async Task<IActionResult> List(int artistId, CancellationToken cancellationToken)
    {
        if (!await db.Artists.AnyAsync(a => a.Id == artistId, cancellationToken))
            return NotFound();

        var rows = await db.ArtistExternalLinks.AsNoTracking()
            .Where(l => l.ArtistId == artistId)
            .OrderBy(l => l.Source).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken);
        return Ok(new ArtistExternalLinkListResponse(rows.Select(ArtistExternalLinkResponse.From).ToList()));
    }

    /// <summary>Links an identifier to the artist (also relinks one that was unlinked). A person's decision.</summary>
    [HttpPost]
    public async Task<IActionResult> Link(
        int artistId, [FromBody] LinkArtistExternalIdRequest request, CancellationToken cancellationToken)
    {
        if (ExternalSources.Normalize(request.Source, request.ExternalId) is not { } normalized)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["externalId"] =
                [
                    "The source must be a lower-case name (letters, digits, '.', '_', '-') and the identifier a non-empty value without whitespace; a MusicBrainz artist ID is a UUID.",
                ],
            }));
        }
        if (!await db.Artists.AnyAsync(a => a.Id == artistId, cancellationToken))
            return NotFound();

        var (source, id) = normalized;
        try
        {
            var (check, link) = await links.LinkManuallyAsync(artistId, source, id, cancellationToken);
            return check switch
            {
                LinkCheck.TakenByOtherArtist => Conflict(ConflictDetails(
                    $"This {source} identifier is already linked to another artist; unlink it there first.")),
                _ => Ok(ArtistExternalLinkResponse.From(link!)),
            };
        }
        catch (DbUpdateException)
        {
            // A concurrent request linked the same identifier first (the unique indexes).
            return Conflict(ConflictDetails("The link conflicts with another change; try again."));
        }
    }

    /// <summary>Unlinks an identifier. The row stays as an unlinked correction, so automation does not link it again.</summary>
    [HttpDelete("{linkId:int}")]
    public async Task<IActionResult> Unlink(int artistId, int linkId, CancellationToken cancellationToken)
    {
        var link = await links.UnlinkAsync(artistId, linkId, cancellationToken);
        return link is null ? NotFound() : Ok(ArtistExternalLinkResponse.From(link));
    }

    private static ProblemDetails ConflictDetails(string detail) =>
        new() { Status = StatusCodes.Status409Conflict, Title = "Conflict", Detail = detail };
}
