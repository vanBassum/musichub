using System.Text.Json;
using MusicHub.Api.ExternalSearch;

namespace MusicHub.Api.Identification;

// Albums are MusicBrainz release groups, editions are its releases. Like the rest of the search, this goes through
// the shared instance, so the rate-limit gate, retries and failure handling apply to one MusicBrainz client.
public sealed partial class MusicBrainzCandidateSearch : IExternalAlbumSource
{
    // MusicBrainz answers at most 100 entries per browse request.
    private const int EditionPageSize = 100;
    private const int MaxEditionPages = 5;

    public async Task<IReadOnlyList<ExternalAlbum>> SearchAlbumsAsync(string term, int limit, int offset, CancellationToken ct)
    {
        var query = $"releasegroup:\"{Quote(term)}\" AND primarytype:album";
        using var json = await GetJsonAsync(
            $"release-group?query={Uri.EscapeDataString(query)}&limit={limit}&offset={offset}&fmt=json", ct);
        return Parse(json, "release-groups", ToExternalAlbum);
    }

    // The type filter keeps studio albums, greatest hits and compilations, and drops singles, EPs and the rest.
    public async Task<IReadOnlyList<ExternalAlbum>> GetArtistAlbumsAsync(string artistExternalId, int limit, CancellationToken ct)
    {
        using var json = await GetJsonAsync(
            $"release-group?artist={Uri.EscapeDataString(artistExternalId)}&type=album&inc=artist-credits&limit={limit}&fmt=json", ct);
        return Parse(json, "release-groups", ToExternalAlbum);
    }

    public async Task<ExternalAlbumEditions?> GetAlbumEditionsAsync(string albumExternalId, CancellationToken ct)
    {
        var id = Uri.EscapeDataString(albumExternalId);
        using var group = await GetJsonOrNullAsync($"release-group/{id}?inc=artist-credits&fmt=json", ct);
        if (group is null)
        {
            return null;
        }
        var album = Shaped(() => ToExternalAlbum(group.RootElement))
            ?? throw new CandidateSearchUnavailableException("MusicBrainz answered in an unexpected shape.");

        var editions = new List<ExternalEdition>();
        var total = 0;
        var read = 0;
        for (var page = 0; page < MaxEditionPages; page++)
        {
            using var releases = await GetJsonAsync(
                $"release?release-group={id}&inc=media&limit={EditionPageSize}&offset={page * EditionPageSize}&fmt=json", ct);
            editions.AddRange(Parse(releases, "releases", ToExternalEdition));
            total = releases.RootElement.TryGetProperty("release-count", out var count) && count.TryGetInt32(out var n) ? n : 0;
            read += EditionPageSize;
            if (read >= total)
            {
                break;
            }
        }
        return new ExternalAlbumEditions(album, editions, Truncated: read < total);
    }

    public async Task<ExternalEditionTrackList?> GetEditionTracksAsync(string editionExternalId, CancellationToken ct)
    {
        using var json = await GetJsonOrNullAsync(
            $"release/{Uri.EscapeDataString(editionExternalId)}?inc=recordings+artist-credits+release-groups&fmt=json", ct);
        return json is null ? null : Shaped(() => ToTrackList(json.RootElement));
    }

    private static T Shaped<T>(Func<T> convert)
    {
        try
        {
            return convert();
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new CandidateSearchUnavailableException("MusicBrainz answered in an unexpected shape.", e);
        }
    }

    private ExternalAlbum? ToExternalAlbum(JsonElement group)
    {
        var id = Text(group, "id");
        var title = Text(group, "title");
        if (id is null || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var secondary = new List<string>();
        if (group.TryGetProperty("secondary-types", out var types) && types.ValueKind == JsonValueKind.Array)
        {
            secondary.AddRange(types.EnumerateArray()
                .Where(t => t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString()))
                .Select(t => t.GetString()!));
        }
        return new ExternalAlbum(
            Provider, id, title, NonEmpty(Text(group, "disambiguation")), $"{SiteUrl}release-group/{id}",
            NonEmpty(Text(group, "primary-type")), secondary, NonEmpty(Text(group, "first-release-date")), Credits(group));
    }

    private ExternalEdition? ToExternalEdition(JsonElement release)
    {
        var id = Text(release, "id");
        var title = Text(release, "title");
        if (id is null || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var declared = Media(release).Select(m => DeclaredTrackCount(m)).ToList();
        return new ExternalEdition(
            Provider, id, title, NonEmpty(Text(release, "disambiguation")), $"{SiteUrl}release/{id}",
            NonEmpty(Text(release, "date")), NonEmpty(Text(release, "country")), NonEmpty(Text(release, "status")),
            NonEmpty(Text(release, "packaging")), NonEmpty(Text(release, "barcode")),
            declared.Count > 0 && declared.All(d => d is not null) ? declared.Sum(d => d!.Value) : null);
    }

    private ExternalEditionTrackList ToTrackList(JsonElement release)
    {
        var edition = ToExternalEdition(release)
            ?? throw new CandidateSearchUnavailableException("MusicBrainz answered in an unexpected shape.");
        var albumId = release.TryGetProperty("release-group", out var group) ? Text(group, "id") : null;

        var tracks = new List<ExternalEditionTrack>();
        var problems = new List<string>();
        var media = Media(release);
        for (var index = 0; index < media.Count; index++)
        {
            var medium = media[index];
            var mediumNumber = medium.TryGetProperty("position", out var p) && p.TryGetInt32(out var position) ? position : index + 1;
            var listed = medium.TryGetProperty("tracks", out var array) && array.ValueKind == JsonValueKind.Array
                ? array.EnumerateArray().ToList()
                : [];
            if (DeclaredTrackCount(medium) is { } declared && listed.Count < declared)
            {
                problems.Add($"Medium {mediumNumber} lists {listed.Count} of {declared} tracks.");
            }
            foreach (var track in listed)
            {
                tracks.Add(ToEditionTrack(track, tracks.Count + 1, mediumNumber, problems));
            }
        }

        if (tracks.Count == 0)
        {
            return new ExternalEditionTrackList(
                edition, albumId, TrackListState.Missing, ["The provider lists no tracks for this edition."], []);
        }
        return new ExternalEditionTrackList(
            edition, albumId, problems.Count == 0 ? TrackListState.Complete : TrackListState.Incomplete, problems, tracks);
    }

    // A track as it stands: a missing title or recording is reported as a problem and left empty, never filled in.
    private ExternalEditionTrack ToEditionTrack(JsonElement track, int position, int medium, List<string> problems)
    {
        var title = NonEmpty(Text(track, "title"));
        var recording = track.TryGetProperty("recording", out var r) ? r : default;
        var recordingId = Text(recording, "id");
        if (title is null)
        {
            problems.Add($"Track {position} has no title.");
        }
        if (recordingId is null)
        {
            problems.Add($"Track {position} has no recording.");
        }
        return new ExternalEditionTrack(
            position, medium, NonEmpty(Text(track, "number")), title,
            track.TryGetProperty("length", out var length) && length.TryGetInt32(out var ms) ? ms : null,
            recordingId, recordingId is null ? null : $"{SiteUrl}recording/{recordingId}", Credits(track));
    }

    private static List<JsonElement> Media(JsonElement release) =>
        release.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Array
            ? media.EnumerateArray().ToList()
            : [];

    private static int? DeclaredTrackCount(JsonElement medium) =>
        medium.TryGetProperty("track-count", out var count) && count.TryGetInt32(out var n) ? n : null;
}
