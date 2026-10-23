namespace MusicHub.Api.Catalog;

/// <param name="TrackCount">The number of distinct tracks the artist is linked to.</param>
public record ArtistResponse(int Id, string Name, int TrackCount);

public record ArtistListResponse(List<ArtistResponse> Items, int Total);

/// <summary>The artist list query, shared by the controller and its provider-translation test.</summary>
public static class ArtistListQuery
{
    public static IQueryable<Artist> Filter(IQueryable<Artist> artists, string? search)
        => SearchTerm.Filter(artists, a => a.Name, search);

    public static IQueryable<ArtistResponse> Page(IQueryable<Artist> artists, int pageSize) => artists
        .OrderBy(a => a.Name).ThenBy(a => a.Id)
        .Take(pageSize)
        .Select(a => new ArtistResponse(a.Id, a.Name, a.Tracks.Select(l => l.TrackId).Distinct().Count()));
}
