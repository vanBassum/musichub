using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// What one run did: discovery (<paramref name="Examined"/> tracks searched, <paramref name="NothingFound"/>,
/// <paramref name="ProviderUnavailable"/>, <paramref name="Skipped"/>, <paramref name="Failed"/>) and enqueueing
/// (<paramref name="Enqueued"/> new download requests, <paramref name="AlreadyQueued"/> tracks that already had one). The
/// transfers are not part of the run: they belong to the queue's requests. <paramref name="Tracks"/> is each track's
/// result (at most <see cref="MissingMusicAcquisition.MaxListed"/>, the counts always cover all of them).
/// </summary>
public sealed record MissingMusicSummary(
    int Examined, int Enqueued, int AlreadyQueued, int NothingFound, int ProviderUnavailable, int Skipped, int Failed,
    IReadOnlyList<TrackAcquisitionResult> Tracks);

/// <summary>
/// The Search for Missing Music run: every track without an available stored file (the library's Missing Music view, not
/// a wishlist) is searched through <see cref="TrackAcquisition.DiscoverAsync"/>, a few at a time, and its best candidate
/// is put in the <see cref="IDownloadQueueService"/>. The run ends when that is done; it never waits for a transfer. A
/// track that already has an active request is not searched again, and a candidate rejected before is not offered again,
/// so repeating a run adds nothing twice. One track's failure never stops the others; cancelling stops at once. It is
/// manual: nothing here schedules it.
/// </summary>
public sealed class MissingMusicAcquisition(
    IServiceScopeFactory scopes, IOptions<MissingMusicOptions> options, ILogger<MissingMusicAcquisition> logger)
{
    public const int MaxListed = 1000;

    public const string NoProviderMessage = "No acquisition provider is configured: set Slskd__BaseUrl and Slskd__ApiKey.";

    /// <summary>Whether a provider is registered. Without one nothing can be searched, so a run is not started.</summary>
    public static bool AcquisitionProviderConfigured(IServiceProvider services) =>
        services.GetService<IAcquisitionProvider>() is not null;

    /// <param name="executionId">The Hangfire execution the downloads and attempts are recorded under; null when not a job.</param>
    /// <param name="total">Told how many tracks the run goes through, before it starts on them.</param>
    public async Task<MissingMusicSummary> RunAsync(string? executionId, Action<int>? total, CancellationToken cancellationToken)
    {
        options.Value.Validate();
        var (ids, queued) = await MissingTracksAsync(cancellationToken);
        logger.LogInformation(
            "Missing music search started: {Count} tracks without a stored file, {Queued} already queued.", ids.Count, queued.Count);
        total?.Invoke(ids.Count);

        var results = new ConcurrentBag<TrackAcquisitionResult>();
        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = options.Value.MaxConcurrentTracks, CancellationToken = cancellationToken },
            async (id, token) => results.Add(queued.Contains(id)
                ? new TrackAcquisitionResult(id, "", "", TrackAcquisitionOutcome.AlreadyQueued, AlreadyQueuedDetail)
                : await DiscoverAndEnqueueAsync(id, executionId, token)));

        var all = results.OrderBy(r => r.TrackId).ToList();
        int Count(TrackAcquisitionOutcome outcome) => all.Count(r => r.Outcome == outcome);
        var summary = new MissingMusicSummary(
            ids.Count, Count(TrackAcquisitionOutcome.Enqueued), Count(TrackAcquisitionOutcome.AlreadyQueued),
            Count(TrackAcquisitionOutcome.NothingFound), Count(TrackAcquisitionOutcome.ProviderUnavailable),
            Count(TrackAcquisitionOutcome.Skipped), Count(TrackAcquisitionOutcome.Failed), all.Take(MaxListed).ToList());
        logger.LogInformation(
            "Missing music search finished: {Examined} examined, {Enqueued} enqueued, {AlreadyQueued} already queued, {NothingFound} nothing found, {Unavailable} provider unavailable, {Skipped} skipped, {Failed} failed.",
            summary.Examined, summary.Enqueued, summary.AlreadyQueued, summary.NothingFound,
            summary.ProviderUnavailable, summary.Skipped, summary.Failed);
        return summary;
    }

    private const string AlreadyQueuedDetail = "The track already has an active download request.";

    /// <summary>The tracks without a stored file, and those of them that already have an active download request.</summary>
    private async Task<(List<int> Ids, HashSet<int> Queued)> MissingTracksAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        if (!AcquisitionProviderConfigured(scope.ServiceProvider))
            throw new InvalidOperationException(NoProviderMessage);

        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        var ids = await db.Tracks.AsNoTracking()
            .Where(t => !t.Files.AsQueryable().Any(TrackResponse.IsStoredAudio))
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
        var queued = await db.DownloadRequests.AsNoTracking()
            .Where(r => DownloadRequestStatuses.Active.Contains(r.Status) && r.TrackId != null)
            .Select(r => r.TrackId!.Value)
            .ToListAsync(cancellationToken);
        return (ids, queued.ToHashSet());
    }

    // Each track has its own scope: a database context is not shared between concurrent tracks.
    private async Task<TrackAcquisitionResult> DiscoverAndEnqueueAsync(int trackId, string? executionId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var discovery = await scope.ServiceProvider.GetRequiredService<TrackAcquisition>()
                .DiscoverAsync(trackId, cancellationToken, executionId);
            if (discovery.Stopped is { } stopped)
                return stopped;

            // The best candidate only: a track has at most one active request. If it is rejected after its download,
            // the next run no longer offers it and enqueues the next best.
            var best = discovery.Fresh[0];
            var queue = scope.ServiceProvider.GetRequiredService<IDownloadQueueService>();
            var enqueued = await queue.EnqueueAsync(new EnqueueDownloadCommand(trackId, best, executionId, discovery.SearchId), cancellationToken);
            return enqueued.Outcome switch
            {
                EnqueueOutcome.Created => discovery.Result(TrackAcquisitionOutcome.Enqueued,
                    $"Queued download request #{enqueued.Request?.Id} ({discovery.Fresh.Count} candidates)."),
                EnqueueOutcome.AlreadyActive => discovery.Result(TrackAcquisitionOutcome.AlreadyQueued, AlreadyQueuedDetail),
                _ => discovery.Result(TrackAcquisitionOutcome.Skipped, "The track was gone when its turn came."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Acquiring track {TrackId} failed.", trackId);
            // Only the exception type: the message may carry paths or connection details.
            var detail = $"Processing failed ({ex.GetType().Name}).";
            await RecordFailureAsync(trackId, detail, executionId);
            return new TrackAcquisitionResult(trackId, "", "", TrackAcquisitionOutcome.Failed, detail);
        }
    }

    // In a scope of its own: the one that failed may hold a context that cannot save. A timeline that cannot be written
    // never hides the failure it was about to report.
    private async Task RecordFailureAsync(int trackId, string detail, string? executionId)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TrackActivityLog>().RecordAsync(
                trackId, TrackActivityKind.SearchFailed, "The search run failed for this track.", CancellationToken.None,
                detail, jobExecutionId: executionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Recording the failure of track {TrackId} in its activity failed.", trackId);
        }
    }
}
