using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace MusicHub.Api.Identification;

/// <summary>
/// Fingerprints audio with Chromaprint and asks AcoustID which recordings match. The AcoustID score is an external
/// service's number: it is kept on the evidence as provenance and is not MusicHub's identification confidence.
/// Every way it can fail to answer is a <see cref="FingerprintUnavailableException"/>; no identity is invented.
/// </summary>
public sealed class AcoustIdFingerprintMatcher(
    HttpClient http, IChromaprintFingerprinter fingerprinter, IOptions<AcoustIdOptions> options) : IAudioFingerprintMatcher
{
    private readonly AcoustIdOptions _options = options.Value;

    public string Source => "acoustid";

    public async Task<IReadOnlyList<EvidenceCandidate>> MatchAsync(Stream audio, string? fileNameHint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new FingerprintUnavailableException("The AcoustID API key is not configured (AcoustId__ApiKey).");
        }

        var fingerprint = await fingerprinter.FingerprintAsync(audio, fileNameHint, ct);
        var response = await LookupAsync(fingerprint, ct);

        var candidates = new List<EvidenceCandidate>();
        var seen = new HashSet<string?>();
        foreach (var result in response.Results ?? [])
        {
            if (result.Score < _options.MinScore)
            {
                continue;
            }
            foreach (var recording in result.Recordings ?? [])
            {
                var artists = (recording.Artists ?? [])
                    .Select(a => a.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n!)
                    .ToList();
                if (string.IsNullOrWhiteSpace(recording.Title) || artists.Count == 0 || !seen.Add(recording.Id))
                {
                    continue;
                }
                var album = recording.ReleaseGroups?.Select(g => g.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
                candidates.Add(new EvidenceCandidate(
                    recording.Title, artists, album,
                    $"AcoustID recording {recording.Id} (result {result.Id}), AcoustID score {result.Score.ToString("0.00", CultureInfo.InvariantCulture)}."));
            }
        }
        return candidates;
    }

    private async Task<LookupResponse> LookupAsync(AudioFingerprintData fingerprint, CancellationToken ct)
    {
        // POST, so the API key and the fingerprint never appear in a URL (and so not in HTTP logs).
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client"] = _options.ApiKey!,
                ["format"] = "json",
                ["meta"] = "recordings releasegroups",
                ["duration"] = fingerprint.DurationSeconds.ToString(CultureInfo.InvariantCulture),
                ["fingerprint"] = fingerprint.Fingerprint,
            }),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
        try
        {
            using var httpResponse = await http.SendAsync(request, timeout.Token);
            if (httpResponse.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new FingerprintUnavailableException("AcoustID rate limit reached; try again later.");
            }
            if (httpResponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new FingerprintUnavailableException(
                    $"AcoustID rejected the request ({(int)httpResponse.StatusCode}); check AcoustId__ApiKey.");
            }
            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new FingerprintUnavailableException($"AcoustID answered with status {(int)httpResponse.StatusCode}.");
            }

            var body = await httpResponse.Content.ReadFromJsonAsync<LookupResponse>(timeout.Token)
                ?? throw new FingerprintUnavailableException("AcoustID answered with an empty response.");
            if (body.Status != "ok")
            {
                throw new FingerprintUnavailableException(body.Error?.Code == 14
                    ? "AcoustID rate limit reached; try again later."
                    : "AcoustID reported an error" + (body.Error?.Code is { } code ? $" (code {code})." : "."));
            }
            return body;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new FingerprintUnavailableException($"AcoustID did not answer within {_options.TimeoutSeconds} seconds.");
        }
        catch (HttpRequestException)
        {
            throw new FingerprintUnavailableException("AcoustID could not be reached.");
        }
        catch (JsonException)
        {
            throw new FingerprintUnavailableException("AcoustID answered with a response that could not be understood.");
        }
    }

    private sealed record LookupResponse(
        [property: JsonPropertyName("status")] string? Status,
        [property: JsonPropertyName("error")] LookupError? Error,
        [property: JsonPropertyName("results")] List<LookupResult>? Results);

    private sealed record LookupError([property: JsonPropertyName("code")] int? Code);

    private sealed record LookupResult(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("recordings")] List<LookupRecording>? Recordings);

    private sealed record LookupRecording(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("artists")] List<LookupArtist>? Artists,
        [property: JsonPropertyName("releasegroups")] List<LookupReleaseGroup>? ReleaseGroups);

    private sealed record LookupArtist([property: JsonPropertyName("name")] string? Name);

    private sealed record LookupReleaseGroup([property: JsonPropertyName("title")] string? Title);
}
