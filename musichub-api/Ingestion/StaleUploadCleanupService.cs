using Microsoft.Extensions.Options;
using MusicHub.Api.Data;

namespace MusicHub.Api.Ingestion;

/// <summary>Runs <see cref="StaleUploadCleanup"/> on a timer once the database is migrated, including after a restart.</summary>
public sealed class StaleUploadCleanupService(
    IServiceScopeFactory scopes,
    MigrationState migrations,
    IOptions<FileIngestionOptions> options,
    ILogger<StaleUploadCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.CleanupInterval;
        using var timer = new PeriodicTimer(interval);
        try
        {
            do
            {
                if (migrations.IsComplete)
                    await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var expired = await scope.ServiceProvider.GetRequiredService<StaleUploadCleanup>().RunAsync(cancellationToken);
            if (expired > 0)
                logger.LogInformation("Stale upload cleanup expired {Count} registration(s).", expired);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Stale upload cleanup failed; it runs again at the next interval.");
        }
    }
}
