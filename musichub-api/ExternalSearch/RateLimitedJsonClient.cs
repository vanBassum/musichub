using System.Diagnostics;
using System.Net;
using System.Text.Json;
using MusicHub.Api.Identification;

namespace MusicHub.Api.ExternalSearch;

/// <summary>How one external service is called: its name in messages, and its pacing and retry limits.</summary>
public sealed record RateLimitedJsonSettings(
    string ServiceName, int TimeoutSeconds, int MinRequestIntervalMs, int MaxRetries, int RetryDelayMs, int MaxRetryDelayMs);

/// <summary>
/// Reads JSON from an external service. Request starts are spaced to the service's rate limit, and transient
/// failures (timeout, 429, 5xx, network) are retried, honouring Retry-After. A request that still fails throws
/// <see cref="CandidateSearchUnavailableException"/>, never an empty result. Messages carry the service name, never
/// the request URL or headers.
/// </summary>
public sealed class RateLimitedJsonClient(HttpClient http, RateLimitedJsonSettings settings)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private TimeSpan? lastRequestAt;

    public async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct) =>
        (await GetJsonCoreAsync(url, notFoundIsNull: false, ct))!;

    /// <summary>Like <see cref="GetJsonAsync"/>, but a 404 (no such entity) answers null instead of failing.</summary>
    public Task<JsonDocument?> GetJsonOrNullAsync(string url, CancellationToken ct) =>
        GetJsonCoreAsync(url, notFoundIsNull: true, ct);

    private async Task<JsonDocument?> GetJsonCoreAsync(string url, bool notFoundIsNull, CancellationToken ct)
    {
        var service = settings.ServiceName;
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
                if (notFoundIsNull && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
                if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
                {
                    throw new CandidateSearchUnavailableException($"{service} answered {(int)response.StatusCode}.");
                }
                failure = $"{service} answered {(int)response.StatusCode}.";
                retryAfter = RetryAfter(response);
            }
            catch (HttpRequestException e)
            {
                failure = $"{service} could not be reached ({e.Message}).";
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                failure = $"{service} did not answer within {settings.TimeoutSeconds} s.";
            }
            catch (JsonException e)
            {
                throw new CandidateSearchUnavailableException($"{service} did not answer with JSON.", e);
            }

            if (attempt >= settings.MaxRetries)
            {
                throw new CandidateSearchUnavailableException($"{failure} Gave up after {attempt + 1} attempt(s).");
            }
            var delay = retryAfter ?? TimeSpan.FromMilliseconds(settings.RetryDelayMs * (attempt + 1));
            var max = TimeSpan.FromMilliseconds(settings.MaxRetryDelayMs);
            await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay > max ? max : delay, ct);
        }
    }

    /// <summary>Spaces request starts at least <see cref="RateLimitedJsonSettings.MinRequestIntervalMs"/> apart; a request may still be in flight when the next starts.</summary>
    private async Task WaitForTurnAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMilliseconds(settings.MinRequestIntervalMs);
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
