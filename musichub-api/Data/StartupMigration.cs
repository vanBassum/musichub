using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MusicHub.Api.Data;

/// <summary>Whether the schema is up to date. The readiness check and the /api gate read it.</summary>
public sealed class MigrationState
{
    private volatile bool _complete;

    public bool IsComplete => _complete;

    public void MarkComplete() => _complete = true;
}

/// <summary>
/// The one way MusicHub applies pending migrations: <see cref="ApplyAsync"/> for a single attempt (the
/// <c>--migrate</c> command), <see cref="RunAsync"/> for startup, which retries while the database is unreachable.
/// </summary>
public static class StartupMigration
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(60);
    private const double MaxJitter = 0.2;

    /// <summary>Applies the pending migrations. Npgsql holds an advisory lock meanwhile, so instances starting together take turns.</summary>
    public static Task ApplyAsync(MusicHubDbContext db, CancellationToken cancellationToken) =>
        db.Database.MigrateAsync(cancellationToken);

    /// <summary>
    /// Runs <paramref name="attempt"/> until it succeeds. An unreachable database is retried without a limit, 5 s doubling to
    /// 60 s between attempts plus up to 20% jitter. Any other failure is deterministic and is rethrown.
    /// </summary>
    public static async Task RunAsync(
        Func<CancellationToken, Task> attempt,
        Func<TimeSpan, CancellationToken, Task> delay,
        ILogger logger,
        Random random,
        CancellationToken cancellationToken)
    {
        for (var n = 0; ; n++)
        {
            try
            {
                await attempt(cancellationToken);
                return;
            }
            catch (Exception ex) when (IsUnreachable(ex) && !cancellationToken.IsCancellationRequested)
            {
                var wait = Backoff(n, random);
                // Only the exception type is logged: its text can hold connection details.
                logger.LogWarning("The database is not reachable ({Error}). Migrating again in {Seconds:0} s.", ex.GetType().Name, wait.TotalSeconds);
                await delay(wait, cancellationToken);
            }
        }
    }

    /// <summary>5, 10, 20, 40, 60, 60, … seconds, plus up to 20% jitter.</summary>
    public static TimeSpan Backoff(int attempt, Random random)
    {
        var seconds = Math.Min(MaxDelay.TotalSeconds, FirstDelay.TotalSeconds * Math.Pow(2, Math.Min(attempt, 10)));
        return TimeSpan.FromSeconds(seconds * (1 + random.NextDouble() * MaxJitter));
    }

    /// <summary>True for a failure to reach the server; a SQL or schema error is not.</summary>
    public static bool IsUnreachable(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                case PostgresException pg:
                    // Only "the server is starting, shutting down or full" is worth waiting for.
                    return pg.SqlState is "57P01" or "57P02" or "57P03" or "53300" || pg.SqlState?.StartsWith("08") == true;
                case SocketException or IOException or TimeoutException:
                    return true;
                case NpgsqlException { IsTransient: true }:
                    return true;
            }
        }
        return false;
    }
}
