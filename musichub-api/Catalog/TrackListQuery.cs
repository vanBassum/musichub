namespace MusicHub.Api.Catalog;

/// <summary>The track list query: filtering and paging, shared by the controller and its provider-translation test.</summary>
public static class TrackListQuery
{
    public static IQueryable<Track> Filter(IQueryable<Track> tracks, string? search, bool? hasAudio)
    {
        var term = search?.Trim().ToLower();
        if (!string.IsNullOrEmpty(term))
        {
            tracks = tracks.Where(t =>
                t.Title.ToLower().Contains(term)
                || (t.Version != null && t.Version.ToLower().Contains(term))
                || t.Artists.Any(a => a.Artist.Name.ToLower().Contains(term)));
        }

        if (hasAudio is { } wanted)
            tracks = tracks.Where(t => t.Files.AsQueryable().Any(TrackResponse.IsStoredAudio) == wanted);

        return tracks;
    }

    public static IQueryable<TrackResponse> Page(IQueryable<Track> tracks, int page, int pageSize) => tracks
        .OrderBy(t => t.Title).ThenBy(t => t.Id)
        .Skip((int)Math.Min((long)(page - 1) * pageSize, int.MaxValue)).Take(pageSize)
        .Select(TrackResponse.Projection);
}
