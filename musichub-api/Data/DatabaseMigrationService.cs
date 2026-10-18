using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace MusicHub.Api.Data;

/// <summary>
/// Applies pending migrations once the app is running. Until it succeeds, readiness is unhealthy and /api answers 503.
/// A migration that fails for a reason other than an unreachable database stops the app with a non-zero exit code.
/// </summary>
public sealed class DatabaseMigrationService(
    IServiceScopeFactory scopes,
    IOptions<DatabaseOptions> options,
    MigrationState state,
    IHostApplicationLifetime lifetime,
    ILogger<DatabaseMigrationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await StartupMigration.RunAsync(Attempt, Task.Delay, logger, Random.Shared, stoppingToken);
            state.MarkComplete();
            logger.LogInformation("Database migrations are up to date.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Never serve against a schema that is not the one this version expects.
            var detail = ex is PostgresException pg ? $" (SQL state {pg.SqlState}: {pg.MessageText})" : "";
            logger.LogCritical("Applying database migrations failed ({Error}){Detail}. Stopping. Fix the cause and restart.",
                ex.GetType().Name, detail);
            Environment.ExitCode = 1;
            lifetime.StopApplication();
        }
    }

    private async Task Attempt(CancellationToken cancellationToken)
    {
        // The API may start without database settings (readiness reports it); keep waiting instead of failing on them.
        if (string.IsNullOrWhiteSpace(options.Value.Host) || string.IsNullOrWhiteSpace(options.Value.Database))
        {
            throw new TimeoutException("Database__Host and Database__Database are not set.");
        }

        using var scope = scopes.CreateScope();
        await StartupMigration.ApplyAsync(scope.ServiceProvider.GetRequiredService<MusicHubDbContext>(), cancellationToken);
    }
}

/// <summary>Unhealthy until the migrations have been applied.</summary>
public sealed class MigrationHealthCheck(MigrationState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.IsComplete ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());
}
