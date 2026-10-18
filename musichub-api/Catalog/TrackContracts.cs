using System.Linq.Expressions;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Catalog;

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
