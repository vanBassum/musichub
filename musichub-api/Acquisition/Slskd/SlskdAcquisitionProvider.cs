using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace MusicHub.Api.Acquisition.Slskd;

/// <summary>
/// Searches and downloads through a Docker-hosted slskd (Soulseek) over its HTTP API. Everything slskd says about a
/// file is the sharing peer's claim; it becomes a candidate with provenance and nothing is picked here. Every way
/// slskd can fail to answer is an <see cref="AcquisitionUnavailableException"/> for a search and a failed outcome
/// for a download, with a detail that names the setting or the step to check. Requests, searches and downloads are
/// each bounded by their own configured timeout.
/// </summary>
public sealed class SlskdAcquisitionProvider(
    HttpClient http, IOptions<SlskdOptions> options, ILogger<SlskdAcquisitionProvider> logger) : IAcquisitionProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> AudioFormats =
        ["mp3", "flac", "ogg", "opus", "m4a", "aac", "wav", "wma", "aiff", "aif", "ape", "wv", "alac"];

    private const int MaxResponses = 100;
    private const int MaxFiles = 1000;

    private readonly SlskdOptions _options = options.Value;

    public string Name => "slskd";

    public async Task<IReadOnlyList<AcquisitionCandidate>> SearchAsync(AcquisitionQuery query, CancellationToken ct)
    {
        var text = $"{query.Artist} {query.Title}".Trim();
        if (string.IsNullOrWhiteSpace(query.Artist) && string.IsNullOrWhiteSpace(query.Title))
        {
            throw new ArgumentException("A search needs an artist or a title.", nameof(query));
        }

        var searchId = Guid.NewGuid();
        try
        {
            await SendAsync(HttpMethod.Post, "api/v0/searches", new
            {
                id = searchId,
                searchText = text,
                searchTimeout = _options.SearchTimeoutSeconds * 1000,
                responseLimit = MaxResponses,
                fileLimit = MaxFiles,
            }, ct);
            await WaitForSearchAsync(searchId, ct);
            var responses = await ReadAsync<List<SearchResponse>>(
                HttpMethod.Get, $"api/v0/searches/{searchId}/responses", ct) ?? [];
            return ToCandidates(responses);
        }
        finally
        {
            await BestEffortAsync(token => SendAsync(HttpMethod.Delete, $"api/v0/searches/{searchId}", null, token));
        }
    }

    public async Task<AcquisitionDownloadOutcome> DownloadAsync(AcquisitionCandidate candidate, CancellationToken ct)
    {
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bound.CancelAfter(TimeSpan.FromSeconds(_options.DownloadTimeoutSeconds));
        HashSet<string> known;
        try
        {
            known = (await FindTransfersAsync(candidate, bound.Token)).Select(t => t.Id).ToHashSet();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Nothing was queued yet, so there is nothing of ours to cancel.
            return Failed(AcquisitionFailure.TimedOut,
                $"The download did not finish within {_options.DownloadTimeoutSeconds} seconds.");
        }
        catch (AcquisitionUnavailableException e)
        {
            return Failed(AcquisitionFailure.Unavailable, e.Message);
        }

        try
        {
            await SendAsync(HttpMethod.Post, $"api/v0/transfers/downloads/{Uri.EscapeDataString(candidate.Source)}",
                new[] { new { filename = candidate.Path, size = candidate.SizeBytes } }, bound.Token);
            return await WaitForDownloadAsync(candidate, known, bound.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await CancelTransfersAsync(candidate, known);
            return Failed(AcquisitionFailure.TimedOut,
                $"The download did not finish within {_options.DownloadTimeoutSeconds} seconds and was cancelled in slskd.");
        }
        catch (OperationCanceledException)
        {
            await CancelTransfersAsync(candidate, known);
            throw;
        }
        catch (AcquisitionUnavailableException e)
        {
            return Failed(AcquisitionFailure.Unavailable, e.Message);
        }
    }

    private async Task WaitForSearchAsync(Guid searchId, CancellationToken ct)
    {
        // slskd ends the search itself after searchTimeout; the extra request timeout is only grace for a slow slskd.
        var limit = TimeSpan.FromSeconds(_options.SearchTimeoutSeconds + _options.RequestTimeoutSeconds);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < limit)
        {
            var state = await ReadAsync<SearchState>(HttpMethod.Get, $"api/v0/searches/{searchId}", ct);
            if (state?.IsComplete == true)
            {
                return;
            }
            await Task.Delay(_options.PollIntervalMs, ct);
        }
        // The answers collected so far are still useful; the search is deleted afterwards.
    }

    private IReadOnlyList<AcquisitionCandidate> ToCandidates(List<SearchResponse> responses) =>
        responses
            .Where(r => !string.IsNullOrWhiteSpace(r.Username))
            .SelectMany(r => (r.Files ?? [])
                .Where(f => !string.IsNullOrWhiteSpace(f.Filename) && f.Size > 0)
                .Select(f => (Response: r, File: f, Format: FormatOf(f.Filename!)))
                .Where(x => AudioFormats.Contains(x.Format))
                .Select(x => new AcquisitionCandidate(
                    Name, x.Response.Username!, x.File.Filename!, x.File.Size, x.Format,
                    x.File.BitRate, x.File.SampleRate, x.File.BitDepth, x.File.Length,
                    x.Response.HasFreeUploadSlot, x.Response.QueueLength)))
            .Take(_options.MaxCandidates)
            .ToList();

    private static string FormatOf(string path) =>
        System.IO.Path.GetExtension(RemotePath.FileName(path)).TrimStart('.').ToLowerInvariant();

    private async Task<AcquisitionDownloadOutcome> WaitForDownloadAsync(
        AcquisitionCandidate candidate, HashSet<string> known, CancellationToken ct)
    {
        while (true)
        {
            await Task.Delay(_options.PollIntervalMs, ct);
            // Only transfers that appeared after we queued ours: an older one for the same file is not ours.
            var transfer = (await FindTransfersAsync(candidate, ct)).LastOrDefault(t => !known.Contains(t.Id));
            if (transfer is null)
            {
                continue;
            }

            var state = transfer.State ?? "";
            if (!state.Contains("Completed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (state.Contains("Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                return Locate(candidate);
            }
            return state.Contains("Rejected", StringComparison.OrdinalIgnoreCase)
                ? Failed(AcquisitionFailure.Rejected,
                    $"{candidate.Source} declined the download ({state}). Try another source.")
                : Failed(AcquisitionFailure.TransferFailed,
                    $"The transfer from {candidate.Source} ended without the file ({state}). Try another source.");
        }
    }

    /// <summary>
    /// slskd saves a download as <c>{downloads}/{last folder of the remote path}/{file name}</c>. The path is built
    /// from the remote path's last two segments only and must stay inside the downloads folder.
    /// </summary>
    private AcquisitionDownloadOutcome Locate(AcquisitionCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(_options.DownloadsPath))
        {
            return Failed(AcquisitionFailure.FileNotAccessible,
                "slskd finished the download, but Slskd__DownloadsPath is not set, so MusicHub cannot read the file.");
        }

        var root = System.IO.Path.GetFullPath(_options.DownloadsPath);
        var segments = RemotePath.Segments(candidate.Path);
        var folder = segments.Length > 1 ? segments[^2] : "";
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, folder, candidate.FileName));
        if (!path.StartsWith(root.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar,
                StringComparison.Ordinal) || !File.Exists(path))
        {
            return Failed(AcquisitionFailure.FileNotAccessible,
                "slskd finished the download, but the file is not in Slskd__DownloadsPath. " +
                "Check that the folder is mounted from slskd's downloads folder.");
        }
        return AcquisitionDownloadOutcome.Completed(Name, new LocalAcquiredFile(candidate.FileName, path));
    }

    private async Task<List<TransferFile>> FindTransfersAsync(AcquisitionCandidate candidate, CancellationToken ct)
    {
        var user = await ReadAsync<TransferUser>(
            HttpMethod.Get, $"api/v0/transfers/downloads/{Uri.EscapeDataString(candidate.Source)}", ct);
        return (user?.Directories ?? [])
            .SelectMany(d => d.Files ?? [])
            .Where(f => f.Filename == candidate.Path && !string.IsNullOrEmpty(f.Id))
            .ToList();
    }

    /// <summary>Cancels and removes the transfers we queued, so a retry isn't blocked by a leftover.</summary>
    private Task CancelTransfersAsync(AcquisitionCandidate candidate, HashSet<string> known) =>
        BestEffortAsync(async token =>
        {
            foreach (var transfer in (await FindTransfersAsync(candidate, token)).Where(t => !known.Contains(t.Id)))
            {
                await SendAsync(HttpMethod.Delete,
                    $"api/v0/transfers/downloads/{Uri.EscapeDataString(candidate.Source)}/{Uri.EscapeDataString(transfer.Id)}?remove=true",
                    null, token);
            }
        });

    /// <summary>Cleanup that must run even after a cancellation and must never hide the real outcome.</summary>
    private async Task BestEffortAsync(Func<CancellationToken, Task> action)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        try
        {
            await action(timeout.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or AcquisitionUnavailableException)
        {
        }
    }

    private AcquisitionDownloadOutcome Failed(AcquisitionFailure failure, string detail) =>
        AcquisitionDownloadOutcome.Failed(Name, failure, detail);

    private async Task<T?> ReadAsync<T>(HttpMethod method, string path, CancellationToken ct)
    {
        using var response = await SendAsync(method, path, null, ct);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (JsonException e)
        {
            throw new AcquisitionUnavailableException("slskd answered with a response that could not be understood.", e);
        }
    }

    /// <summary>
    /// One request; any answer but a success is an <see cref="AcquisitionUnavailableException"/>. A 429 means slskd
    /// refused the request before acting on it, so it is repeated (same body, so the same search id or transfer) after
    /// <c>Retry-After</c> or an exponential backoff, within the configured attempts and maximum delay.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        for (var retry = 0; ; retry++)
        {
            var response = await SendOnceAsync(method, path, body, ct);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var status = response.StatusCode;
            var retryAfter = response.Headers.RetryAfter;
            response.Dispose();
            if (status != HttpStatusCode.TooManyRequests)
            {
                throw new AcquisitionUnavailableException(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? $"slskd rejected the API key ({(int)status}); check Slskd__ApiKey."
                    : $"slskd answered with status {(int)status}.");
            }

            var operation = Operation(method, path);
            if (retry >= _options.RateLimitMaxRetries)
            {
                logger.LogWarning("slskd {Operation} answered 429 and was given up after {Retries} retries.", operation, retry);
                throw new AcquisitionUnavailableException(
                    $"slskd answered with status 429 (too many requests) for {operation} and kept doing so after " +
                    $"{retry} retries; try again later or lower MissingMusic__MaxConcurrentTracks.");
            }

            var delay = RateLimitDelay(retryAfter, retry);
            logger.LogWarning("slskd {Operation} answered 429; retry {Retry} of {Max} in {DelayMs} ms.",
                operation, retry + 1, _options.RateLimitMaxRetries, (int)delay.TotalMilliseconds);
            await Task.Delay(delay, ct);
        }
    }

    /// <summary>The server's <c>Retry-After</c> (seconds or a date) when it gave one, else a doubling backoff; never over the maximum.</summary>
    private TimeSpan RateLimitDelay(RetryConditionHeaderValue? retryAfter, int retry)
    {
        var requested = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null)
            ?? TimeSpan.FromMilliseconds(_options.RateLimitBaseDelayMs * Math.Pow(2, retry));
        var max = TimeSpan.FromMilliseconds(_options.RateLimitMaxDelayMs);
        return requested < TimeSpan.Zero ? TimeSpan.Zero : requested > max ? max : requested;
    }

    /// <summary>The method and the resource kind only: ids, user names and file names never reach a log.</summary>
    private static string Operation(HttpMethod method, string path)
    {
        var segments = path.Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(2).ToArray();
        var kept = segments.Length > 0 && segments[0] == "transfers" ? 2 : 1;
        return $"{method.Method} {string.Join('/', segments.Take(kept))}";
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        try
        {
            return await http.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new AcquisitionUnavailableException($"slskd did not answer within {_options.RequestTimeoutSeconds} seconds.");
        }
        catch (HttpRequestException e)
        {
            throw new AcquisitionUnavailableException(
                "slskd could not be reached; check Slskd__BaseUrl and that slskd is running.", e);
        }
    }

    private sealed record SearchState(bool IsComplete);

    private sealed record SearchResponse(
        string? Username, bool HasFreeUploadSlot, int? QueueLength, List<SearchFile>? Files);

    private sealed record SearchFile(
        string? Filename, long Size, int? BitRate, int? SampleRate, int? BitDepth, int? Length);

    private sealed record TransferUser(List<TransferDirectory>? Directories);

    private sealed record TransferDirectory(List<TransferFile>? Files);

    private sealed record TransferFile(string Id, string? Filename, string? State);
}
