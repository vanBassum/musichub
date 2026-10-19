namespace MusicHub.Api.Catalog;

/// <summary>The track list query: filtering and paging, shared by the controller and its provider-translation test.</summary>
public static class TrackListQuery
{
    /// <summary>The fields a search can be limited to: "title" (title and version) and "artist".</summary>
    public static readonly IReadOnlyList<string> SearchFields = ["title", "artist"];

    /// <summary>Parses a comma-separated field list; null when it is empty or names an unknown field.</summary>
    public static (bool Title, bool Artist)? ParseFields(string? fields)
    {
        if (fields is null) return (true, true);
        var names = fields.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.ToLowerInvariant()).ToList();
        if (names.Count == 0 || names.Any(n => !SearchFields.Contains(n))) return null;
        return (names.Contains("title"), names.Contains("artist"));
    }

    public static IQueryable<Track> Filter(
        IQueryable<Track> tracks, string? search, bool? hasAudio, (bool Title, bool Artist)? fields = null)
    {
        var (inTitle, inArtist) = fields ?? (true, true);
        var term = search?.Trim().ToLower();
        if (!string.IsNullOrEmpty(term))
        {
            tracks = tracks.Where(t =>
                (inTitle && (t.Title.ToLower().Contains(term)
                    || (t.Version != null && t.Version.ToLower().Contains(term))))
                || (inArtist && t.Artists.Any(a => a.Artist.Name.ToLower().Contains(term))));
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
