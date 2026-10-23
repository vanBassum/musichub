using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.ExternalSearch;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Controllers;

public record ExternalAlbumListResponse(string Provider, IReadOnlyList<ExternalAlbum> Items);

/// <param name="Truncated">The provider has more editions than are listed.</param>
public record ExternalAlbumEditionsResponse(
    string Provider, ExternalAlbum Album, IReadOnlyList<ExternalEdition> Editions, bool Truncated, EditionSuggestion Suggestion);

public record ExternalEditionTracksResponse(string Provider, ExternalEditionTrackList Edition);

/// <summary>
/// Existing albums of the external metadata provider, their editions and each edition's ordered track list.
/// Read-only: nothing is registered in the catalog and nothing is cached. A missing, unreachable or rate-limited
/// provider answers 503, which an empty list (200) never does; an album or edition the provider doesn't have is 404.
/// </summary>
[ApiController]
[Route("api/external")]
public class ExternalAlbumsController(IExternalAlbumSource? provider = null) : ControllerBase
{
    /// <summary>Searches albums (studio albums, greatest hits, compilations) by title.</summary>
    [HttpGet("albums")]
    public async Task<IActionResult> Search(
        [FromQuery] string? search, [FromQuery] int limit = ExternalResults.DefaultListLimit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["search"] = ["Search text is required."],
            }));
        }
        return this.InvalidLimit(limit) ?? await Read(
            p => p.SearchAlbumsAsync(search.Trim(), limit, cancellationToken),
            items => new ExternalAlbumListResponse(provider!.Provider, items));
    }

    /// <summary>The albums the provider credits to the artist, as it lists them (no ranking).</summary>
    [HttpGet("artists/{externalId}/albums")]
    public async Task<IActionResult> ArtistAlbums(
        string externalId, [FromQuery] int limit = ExternalResults.DefaultListLimit, CancellationToken cancellationToken = default)
    {
        return this.InvalidLimit(limit) ?? Check(externalId, "artist") ?? await Read(
            p => p.GetArtistAlbumsAsync(externalId, limit, cancellationToken),
            items => new ExternalAlbumListResponse(provider!.Provider, items));
    }

    /// <summary>One album with its editions, and the edition suggested as the standard one only when the evidence singles it out.</summary>
    [HttpGet("albums/{externalId}/editions")]
    public async Task<IActionResult> Editions(string externalId, CancellationToken cancellationToken)
    {
        return Check(externalId, "album") ?? await ReadOne(
            p => p.GetAlbumEditionsAsync(externalId, cancellationToken),
            album => new ExternalAlbumEditionsResponse(
                provider!.Provider, album.Album, album.Editions, album.Truncated, EditionSuggestion.For(album)));
    }

    /// <summary>The ordered tracks of one edition. A missing or incomplete list is flagged in its state, never filled in.</summary>
    [HttpGet("editions/{externalId}/tracks")]
    public async Task<IActionResult> Tracks(string externalId, CancellationToken cancellationToken)
    {
        return Check(externalId, "edition") ?? await ReadOne(
            p => p.GetEditionTracksAsync(externalId, cancellationToken),
            list => new ExternalEditionTracksResponse(provider!.Provider, list));
    }

    private Task<IActionResult> ReadOne<T>(Func<IExternalAlbumSource, Task<T?>> read, Func<T, object> respond)
        where T : class =>
        Run(async p => await read(p) is { } found ? Ok(respond(found)) : NotFound());

    private Task<IActionResult> Read<T>(Func<IExternalAlbumSource, Task<T>> read, Func<T, object> respond) =>
        Run(async p => Ok(respond(await read(p))));

    private Task<IActionResult> Run(Func<IExternalAlbumSource, Task<IActionResult>> answer) =>
        this.WithProvider(provider, answer);

    // Null when the identifier is one of the provider's valid ones, or when there is no provider: Run answers that.
    private IActionResult? Check(string externalId, string what) =>
        provider is null ? null : this.InvalidId(provider.Provider, externalId, what);
}
