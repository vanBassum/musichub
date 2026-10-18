using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MusicHub.Api.Identification;

/// <summary>
/// Searches MusicBrainz recordings by the title and artists the tags name. The tags are only query hints: every
/// answer is a candidate with MusicBrainz's own identifiers, URLs and details, one per recording and release, and
/// nothing is picked here. Requests are spaced to MusicBrainz's rate limit, and transient failures are retried; a
/// search that still fails throws <see cref="CandidateSearchUnavailableException"/>, never an empty result.
/// </summary>
public sealed class MusicBrainzCandidateSearch : ICandidateSearch
{
    public const string HttpClientName = "musicbrainz";

    private const string SiteUrl = "https://musicbrainz.org/";

    private readonly HttpClient http;
    private readonly MusicBrainzOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private TimeSpan? lastRequestAt;

    public MusicBrainzCandidateSearch(HttpClient http, MusicBrainzOptions options)
    {
        this.http = http;
        this.options = options;
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
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl))
        {
            throw new InvalidOperationException("Identification__MusicBrainz__BaseUrl must be an absolute URL.");
        }
        if (options.TimeoutSeconds < 1 || options.MaxRecordings < 1 || options.MaxReleasesPerRecording < 1
            || options.MaxRetries < 0 || options.MinRequestIntervalMs < 0 || options.RetryDelayMs < 0 || options.MaxRetryDelayMs < 0)
        {
            throw new InvalidOperationException("Identification__MusicBrainz has an out-of-range numeric setting.");
        }

        var http = factory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MusicHub", "1.0"));
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"( {options.Contact!.Trim()} )"));
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
        var artists = ArtistNames(recording);
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
                new EvidenceSourceReference(id, recordingUrl, null, null, string.Join("; ", [.. details, "no release listed"])));
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
                    string.Join("; ", releaseDetails)));
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

    private static List<string> ArtistNames(JsonElement recording)
    {
        var names = new List<string>();
        if (recording.TryGetProperty("artist-credit", out var credits) && credits.ValueKind == JsonValueKind.Array)
        {
            foreach (var credit in credits.EnumerateArray())
            {
                var name = Text(credit, "name");
                if (string.IsNullOrWhiteSpace(name) && credit.TryGetProperty("artist", out var artist))
                {
                    name = Text(artist, "name");
                }
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }
        }
        return names;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // Inside a quoted Lucene phrase only the quote and the backslash are special.
    private static string Quote(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            string failure;
            TimeSpan? retryAfter = null;
            try
            {
                await WaitForTurnAsync(ct);
                using var response = await http.GetAsync(url, ct);
                if (response.IsSuccessStatusCode)
                {
                    return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                }
                if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
                {
                    throw new CandidateSearchUnavailableException($"MusicBrainz answered {(int)response.StatusCode}.");
                }
                failure = $"MusicBrainz answered {(int)response.StatusCode}.";
                retryAfter = RetryAfter(response);
            }
            catch (HttpRequestException e)
            {
                failure = $"MusicBrainz could not be reached ({e.Message}).";
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                failure = $"MusicBrainz did not answer within {options.TimeoutSeconds} s.";
            }
            catch (JsonException e)
            {
                throw new CandidateSearchUnavailableException("MusicBrainz did not answer with JSON.", e);
            }

            if (attempt >= options.MaxRetries)
            {
                throw new CandidateSearchUnavailableException($"{failure} Gave up after {attempt + 1} attempt(s).");
            }
            var delay = retryAfter ?? TimeSpan.FromMilliseconds(options.RetryDelayMs * (attempt + 1));
            var max = TimeSpan.FromMilliseconds(options.MaxRetryDelayMs);
            await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay > max ? max : delay, ct);
        }
    }

    /// <summary>Spaces request starts at least <see cref="MusicBrainzOptions.MinRequestIntervalMs"/> apart; a request may still be in flight when the next starts.</summary>
    private async Task WaitForTurnAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(options.MinRequestIntervalMs);
        await gate.WaitAsync(ct);
        try
        {
            if (lastRequestAt is { } last)
            {
                var wait = last + interval - clock.Elapsed;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, ct);
                }
            }
        }
        finally
        {
            lastRequestAt = clock.Elapsed;
            gate.Release();
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return delta;
        }
        return header?.Date is { } date ? date - DateTimeOffset.UtcNow : null;
    }
}
