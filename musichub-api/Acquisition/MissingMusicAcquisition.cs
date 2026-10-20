using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// What one run did. <paramref name="Tracks"/> is each track's result (at most <see cref="MissingMusicAcquisition.MaxListed"/>,
/// the counts always cover all of them); the downloads of the run are the permanent <see cref="AcquisitionDownload"/> rows.
/// </summary>
public sealed record MissingMusicSummary(
    int Examined, int Imported, int NothingFound, int NoAcceptableCandidate, int ProviderUnavailable, int Skipped, int Failed,
    IReadOnlyList<TrackAcquisitionResult> Tracks);

/// <summary>
/// The Search for Missing Music run: every track without an available stored file (the library's Missing Music view, not
/// a wishlist) is acquired through <see cref="TrackAcquisition"/>, a few at a time. One track's failure never stops the
/// others; cancelling stops at once and leaves every finished track as it is, so a new run simply starts over with
/// what is still missing. It is manual: nothing here schedules it.
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
        var ids = await MissingTrackIdsAsync(cancellationToken);
        logger.LogInformation("Missing music search started: {Count} tracks without a stored file.", ids.Count);
        total?.Invoke(ids.Count);

        var results = new ConcurrentBag<TrackAcquisitionResult>();
        await Parallel.ForEachAsync(
            ids,
            new ParallelOptions { MaxDegreeOfParallelism = options.Value.MaxConcurrentTracks, CancellationToken = cancellationToken },
            async (id, token) => results.Add(await AcquireAsync(id, executionId ?? "", token)));

        var all = results.OrderBy(r => r.TrackId).ToList();
        int Count(TrackAcquisitionOutcome outcome) => all.Count(r => r.Outcome == outcome);
        var summary = new MissingMusicSummary(
            ids.Count, Count(TrackAcquisitionOutcome.Imported), Count(TrackAcquisitionOutcome.NothingFound),
            Count(TrackAcquisitionOutcome.NoAcceptableCandidate), Count(TrackAcquisitionOutcome.ProviderUnavailable),
            Count(TrackAcquisitionOutcome.Skipped), Count(TrackAcquisitionOutcome.Failed), all.Take(MaxListed).ToList());
        logger.LogInformation(
            "Missing music search finished: {Examined} examined, {Imported} imported, {NothingFound} nothing found, {None} none accepted, {Unavailable} provider unavailable, {Skipped} skipped, {Failed} failed.",
            summary.Examined, summary.Imported, summary.NothingFound, summary.NoAcceptableCandidate,
            summary.ProviderUnavailable, summary.Skipped, summary.Failed);
        return summary;
    }

    private async Task<List<int>> MissingTrackIdsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        if (!AcquisitionProviderConfigured(scope.ServiceProvider))
            throw new InvalidOperationException(NoProviderMessage);

        var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
        return await db.Tracks.AsNoTracking()
            .Where(t => !t.Files.AsQueryable().Any(TrackResponse.IsStoredAudio))
            .OrderBy(t => t.Id)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
    }

    // Each track has its own scope: a database context is not shared between concurrent tracks.
    private async Task<TrackAcquisitionResult> AcquireAsync(int trackId, string executionId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<TrackAcquisition>().RunAsync(trackId, executionId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Acquiring track {TrackId} failed.", trackId);
            // Only the exception type: the message may carry paths or connection details.
            return new TrackAcquisitionResult(trackId, "", "", TrackAcquisitionOutcome.Failed, $"Processing failed ({ex.GetType().Name}).");
        }
    }
}
