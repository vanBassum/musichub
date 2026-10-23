using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;

namespace MusicHub.Api.Catalog;

public record AlbumExternalIdResponse(string Provider, string ExternalId);

/// <param name="Position">The 1-based place on the edition.</param>
public record AlbumEditionTrackResponse(int Position, TrackResponse Track);

/// <param name="TrackCount">The tracks on the edition.</param>
/// <param name="AvailableTrackCount">Derived: the tracks of the edition that have stored audio linked (hasAudio).</param>
public record AlbumEditionResponse(
    int Id,
    string? Name,
    string? ReleaseDate,
    DateTimeOffset CreatedAt,
    List<AlbumExternalIdResponse> ExternalIds,
    int TrackCount,
    int AvailableTrackCount,
    List<AlbumEditionTrackResponse> Tracks);

public record AlbumResponse(int Id, string Title, DateTimeOffset CreatedAt, List<AlbumEditionResponse> Editions);

public record AlbumListItemResponse(int Id, string Title, DateTimeOffset CreatedAt, int EditionCount);

/// <summary>The read side of albums: it lists them and assembles an album with its editions and their ordered tracks.</summary>
public static class AlbumQueries
{
    public static IQueryable<Album> Filter(IQueryable<Album> albums, string? search)
    {
        var term = search?.Trim().ToLower();
        return string.IsNullOrEmpty(term) ? albums : albums.Where(a => a.Title.ToLower().Contains(term));
    }

    public static IQueryable<AlbumListItemResponse> Page(IQueryable<Album> albums, int page, int pageSize) => albums
        .OrderBy(a => a.Title).ThenBy(a => a.Id)
        .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize)
        .Select(a => new AlbumListItemResponse(a.Id, a.Title, a.CreatedAt, a.Editions.Count));

    /// <summary>
    /// The album with its editions, each with its tracks in position order. The track data is the library's own
    /// <see cref="TrackResponse"/>, so availability follows the same rule everywhere. Null when there is no such album.
    /// </summary>
    public static async Task<AlbumResponse?> FindAsync(MusicHubDbContext db, int id, CancellationToken cancellationToken)
    {
        var album = await db.Albums.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.CreatedAt,
                Editions = a.Editions.OrderBy(e => e.Id).Select(e => new
                {
                    e.Id,
                    e.Name,
                    e.ReleaseDate,
                    e.CreatedAt,
                    ExternalIds = e.ExternalIds.OrderBy(x => x.Id)
                        .Select(x => new AlbumExternalIdResponse(x.Provider, x.ExternalId)).ToList(),
                    Tracks = e.Tracks.OrderBy(t => t.Position).Select(t => new { t.Position, t.TrackId }).ToList(),
                }).ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (album is null) return null;

        var trackIds = album.Editions.SelectMany(e => e.Tracks).Select(t => t.TrackId).Distinct().ToList();
        var tracks = await db.Tracks.AsNoTracking()
            .Where(t => trackIds.Contains(t.Id))
            .Select(TrackResponse.Projection)
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        var editions = album.Editions.Select(e =>
        {
            var entries = e.Tracks.Select(t => new AlbumEditionTrackResponse(t.Position, tracks[t.TrackId])).ToList();
            return new AlbumEditionResponse(
                e.Id, e.Name, e.ReleaseDate, e.CreatedAt, e.ExternalIds,
                entries.Count, entries.Count(t => t.Track.HasAudio), entries);
        }).ToList();
        return new AlbumResponse(album.Id, album.Title, album.CreatedAt, editions);
    }
}
