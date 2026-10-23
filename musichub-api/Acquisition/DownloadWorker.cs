using Microsoft.Extensions.Options;
using MusicHub.Api.Data;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// The background worker of the durable download queue. Once the database is migrated it puts back what a restart
/// interrupted, then runs <see cref="DownloadWorkerOptions.MaxConcurrentDownloads"/> loops that each claim one request
/// at a time through <see cref="IDownloadQueueService"/> (the database settles who gets a request, so loops, and
/// instances, never process one twice). A queued request goes to <see cref="DownloadProcessor"/>; a downloaded one,
/// including every one that was already downloaded when the application started, to
/// <see cref="DownloadHandoffProcessor"/>, which identifies and imports it without downloading again. Stopping cancels
/// the loops; the transfers stay in slskd and are followed again after the restart.
/// </summary>
public sealed class DownloadWorker(
    IServiceScopeFactory scopes,
    MigrationState migrations,
    IOptions<DownloadWorkerOptions> options,
    ILogger<DownloadWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        settings.Validate();
        var poll = TimeSpan.FromSeconds(settings.PollIntervalSeconds);
        try
        {
            while (!migrations.IsComplete)
                await Task.Delay(poll, stoppingToken);

            await RequeueInterruptedAsync(poll, stoppingToken);
            await Task.WhenAll(Enumerable.Range(0, settings.MaxConcurrentDownloads)
                .Select(_ => RunLoopAsync(poll, stoppingToken))
                .Append(RunResearchLoopAsync(poll, stoppingToken)));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RequeueInterruptedAsync(TimeSpan retryAfter, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var requeued = await scope.ServiceProvider.GetRequiredService<IDownloadQueueService>()
                    .RequeueInterruptedAsync(ct);
                if (requeued > 0)
                    logger.LogInformation("{Count} interrupted download(s) were queued again.", requeued);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Requeueing interrupted downloads failed; trying again.");
                await Task.Delay(retryAfter, ct);
            }
        }
    }

    private async Task RunLoopAsync(TimeSpan poll, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!await ProcessNextAsync(ct))
                await Task.Delay(poll, ct);
        }
    }

    /// <summary>Runs the scheduled fresh searches of tracks that ran out of candidates, when they are due.</summary>
    private async Task RunResearchLoopAsync(TimeSpan poll, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AcquisitionFailover>().RunDueAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Running the scheduled searches failed; trying again.");
            }
            await Task.Delay(poll, ct);
        }
    }

    /// <summary>
    /// Claims and processes one request: a downloaded file waiting for its handoff first (so files do not pile up), else a
    /// queued download. False when there was none or the attempt could not even start.
    /// </summary>
    private async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var services = scope.ServiceProvider;
            var queue = services.GetRequiredService<IDownloadQueueService>();
            if (await queue.ClaimNextDownloadedAsync(ct) is { } handoff)
            {
                await services.GetRequiredService<DownloadHandoffProcessor>().ProcessAsync(handoff, ct);
                return true;
            }
            if (await queue.ClaimNextAsync(ct) is not { } claim)
                return false;

            await services.GetRequiredService<DownloadProcessor>().ProcessAsync(claim, ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A request that was claimed stays Downloading (or Processing) and is put back at the next start.
            logger.LogError(ex, "Processing the download queue failed; trying again.");
            return false;
        }
    }
}
