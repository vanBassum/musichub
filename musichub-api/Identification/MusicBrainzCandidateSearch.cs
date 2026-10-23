using System.Globalization;
using System.Text.Json;
using MusicHub.Api.ExternalSearch;

namespace MusicHub.Api.Identification;

/// <summary>
/// Searches MusicBrainz recordings by the title and artists the tags name. The tags are only query hints: every
/// answer is a candidate with MusicBrainz's own identifiers, URLs and details, one per recording and release, and
/// nothing is picked here. Requests are spaced to MusicBrainz's rate limit, and transient failures are retried; a
/// search that still fails throws <see cref="CandidateSearchUnavailableException"/>, never an empty result.
/// </summary>
public sealed partial class MusicBrainzCandidateSearch : ICandidateSearch
{
    public const string HttpClientName = "musicbrainz";

    private const string SiteUrl = "https://musicbrainz.org/";

    private readonly MusicBrainzOptions options;
    private readonly RateLimitedJsonClient client;

    public MusicBrainzCandidateSearch(HttpClient http, MusicBrainzOptions options)
    {
        this.options = options;
        client = new RateLimitedJsonClient(http, RateLimitedJsonSettings.From("MusicBrainz", options));
    }

    public string Source => "musicbrainz";

    /// <summary>Builds the search from configuration; throws when the options cannot work, so a bad setup is loud.</summary>
    public static MusicBrainzCandidateSearch Create(MusicBrainzOptions options, IHttpClientFactory factory)
    {
        if (string.IsNullOrWhiteSpace(options.Contact))
        {
            throw new InvalidOperationException(
                "MusicBrainz is not configured: Identification__MusicBrainz__Contact (an email address or URL) is required when it is enabled.");
        }
        var http = RateLimitedJsonClient.CreateHttpClient(
            factory, HttpClientName, options, "Identification__MusicBrainz",
            options.MaxRecordings < 1 || options.MaxReleasesPerRecording < 1);
        return new MusicBrainzCandidateSearch(http, options);
    }

    public async Task<IReadOnlyList<EvidenceCandidate>> SearchAsync(
        string title, IReadOnlyList<string> artists, CancellationToken ct)
    {
        var query = $"recording:\"{Quote(title)}\" AND " + string.Join(" AND ", artists.Select(a => $"artist:\"{Quote(a)}\""));
        var url = $"recording?query={Uri.EscapeDataString(query)}&limit={options.MaxRecordings}&fmt=json";

        using var json = await GetJsonAsync(url, ct);
        var candidates = new List<EvidenceCandidate>();
        try
        {
            if (json.RootElement.TryGetProperty("recordings", out var recordings) && recordings.ValueKind == JsonValueKind.Array)
            {
                foreach (var recording in recordings.EnumerateArray())
                {
                    candidates.AddRange(ToCandidates(recording));
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new CandidateSearchUnavailableException("MusicBrainz answered in an unexpected shape.", e);
        }
        return candidates;
    }

    private IEnumerable<EvidenceCandidate> ToCandidates(JsonElement recording)
    {
        var id = Text(recording, "id");
        var title = Text(recording, "title");
        var (artists, artistReferences) = ArtistCredits(recording);
        // No id, title or artist: nothing to name, so nothing to offer.
        if (id is null || string.IsNullOrWhiteSpace(title) || artists.Count == 0)
        {
            yield break;
        }

        var details = new List<string>();
        if (recording.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number)
        {
            details.Add($"MusicBrainz search score {score.GetInt32().ToString(CultureInfo.InvariantCulture)}");
        }
        if (Text(recording, "disambiguation") is { Length: > 0 } disambiguation)
        {
            details.Add($"disambiguation \"{disambiguation}\"");
        }
        var recordingUrl = $"{SiteUrl}recording/{id}";

        var releases = recording.TryGetProperty("releases", out var r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Take(options.MaxReleasesPerRecording).ToList()
            : [];
        if (releases.Count == 0)
        {
            yield return new EvidenceCandidate(title, artists, null, Reference:
                new EvidenceSourceReference(id, recordingUrl, null, null, string.Join("; ", [.. details, "no release listed"])),
                ArtistReferences: artistReferences);
            yield break;
        }

        foreach (var release in releases)
        {
            var releaseId = Text(release, "id");
            var releaseDetails = ReleaseDetails(release, details);
            yield return new EvidenceCandidate(
                title,
                artists,
                Text(release, "title"),
                Reference: new EvidenceSourceReference(
                    id,
                    recordingUrl,
                    releaseId,
                    releaseId is null ? null : $"{SiteUrl}release/{releaseId}",
                    string.Join("; ", releaseDetails)),
                ArtistReferences: artistReferences);
        }
    }

    private static List<string> ReleaseDetails(JsonElement release, List<string> recordingDetails)
    {
        var details = new List<string>(recordingDetails);
        foreach (var name in new[] { "status", "date", "country" })
        {
            if (Text(release, name) is { Length: > 0 } value)
            {
                details.Add($"release {name} {value}");
            }
        }
        if (release.TryGetProperty("release-group", out var group) && Text(group, "primary-type") is { Length: > 0 } type)
        {
            details.Add($"release type {type}");
        }
        return details;
    }

    private (List<string> Names, List<ArtistReference> References) ArtistCredits(JsonElement recording)
    {
        var names = new List<string>();
        var references = new List<ArtistReference>();
        if (recording.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
        {
            foreach (var credit in credits.EnumerateArray())
            {
                var artist = credit.TryGetProperty("artist", out var a) ? a : default;
                var name = Text(credit, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = Text(artist, "name");
                }
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                    if (Text(artist, "id") is { Length: > 0 } artistId)
                    {
                        references.Add(new ArtistReference(Source, artistId, name));
                    }
                }
            }
        }
        return (names, references);
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // Inside a quoted Lucene phrase only the quote and the backslash are special.
    private static string Quote(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct) => client.GetJsonAsync(url, ct);

    private Task<JsonDocument?> GetJsonOrNullAsync(string url, CancellationToken ct) => client.GetJsonOrNullAsync(url, ct);
}
