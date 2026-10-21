using Hangfire;
using Microsoft.Extensions.Options;
using MusicHub.Api.Data;

namespace MusicHub.Api.Jobs;

/// <summary>
/// Starts the Hangfire server once the database is migrated, and stops it gracefully with the host: running jobs get
/// <see cref="HangfireOptions.ShutdownTimeoutSeconds"/> to finish, the rest stays in PostgreSQL and runs after the restart.
/// A storage that cannot be created (database down) is retried; like the migrations, it never stops the app.
/// </summary>
public sealed class HangfireServerService(
    IJobStorageFactory storageFactory,
    JobEngine engine,
    JobSchedule schedule,
    MigrationState migrations,
    IOptions<HangfireOptions> options,
    IServiceScopeFactory scopes,
    ILogger<HangfireServerService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.Validate() is { } problem)
        {
            logger.LogError("Background jobs are disabled: {Problem}", problem);
            return;
        }

        try
        {
            while (!migrations.IsComplete)
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);

            var storage = await CreateStorageAsync(stoppingToken);
            ConfigureRetries(settings.RetryAttempts);
            GlobalConfiguration.Configuration.UseStorage(storage).WithJobExpirationTimeout(TimeSpan.FromHours(settings.RetentionHours));

            using var server = new BackgroundJobServer(
                new BackgroundJobServerOptions
                {
                    WorkerCount = settings.WorkerCount,
                    ShutdownTimeout = TimeSpan.FromSeconds(settings.ShutdownTimeoutSeconds),
                    Activator = new ScopedJobActivator(scopes),
                },
                storage);
            schedule.Apply(storage);
            engine.Publish(storage, new BackgroundJobClient(storage));
            logger.LogInformation("Background jobs are running ({Workers} workers, {Retries} retries).",
                settings.WorkerCount, settings.RetryAttempts);

            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }

            logger.LogInformation("Stopping background jobs.");
            // Disposing the server waits for running jobs, up to the shutdown timeout.
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task<JobStorage> CreateStorageAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                return storageFactory.Create();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Only the exception type is logged: its text can hold connection details.
                logger.LogWarning("The job storage could not be created ({Error}). Trying again in {Seconds:0} s.",
                    ex.GetType().Name, RetryDelay.TotalSeconds);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    /// <summary>Hangfire keeps its retry policy as a global filter: replace the default (10 attempts) with the configured one.</summary>
    private static void ConfigureRetries(int attempts)
    {
        foreach (var filter in GlobalJobFilters.Filters.Where(f => f.Instance is AutomaticRetryAttribute).ToList())
            GlobalJobFilters.Filters.Remove(filter.Instance);
        GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = attempts, OnAttemptsExceeded = AttemptsExceededAction.Fail });
    }
}
