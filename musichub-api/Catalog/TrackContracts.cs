using System.Linq.Expressions;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Catalog;

/// <summary>
/// An artist on a track: an existing artist by <see cref="ArtistId"/>, or by <see cref="Name"/>
/// (reusing the artist with that name, ignoring case, or creating it). Exactly one of the two.
/// </summary>
public record TrackArtistRequest(int? ArtistId, string? Name, string? Role);

/// <summary>Body of creating a track and of replacing its metadata.</summary>
public record TrackRequest(string? Title, string? Version, List<TrackArtistRequest>? Artists);

public record TrackArtistResponse(int ArtistId, string Name, string? Role);

/// <param name="HasAudio">Derived: at least one linked file whose upload is <see cref="FileStatus.Stored"/>.</param>
public record TrackResponse(
    int Id,
    string Title,
    string? Version,
    DateTimeOffset CreatedAt,
    List<TrackArtistResponse> Artists,
    bool HasAudio)
{
    public static readonly Expression<Func<Track, TrackResponse>> Projection = t => new TrackResponse(
        t.Id,
        t.Title,
        t.Version,
        t.CreatedAt,
        t.Artists.OrderBy(a => a.Id)
            .Select(a => new TrackArtistResponse(a.ArtistId, a.Artist.Name, a.Role))
            .ToList(),
        t.Files.Any(f => f.Status == FileStatus.Stored));
}

public record PagedResponse<T>(List<T> Items, int Total, int Page, int PageSize);
