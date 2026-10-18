using Microsoft.Extensions.Logging.Abstractions;
using MusicHub.Api.Data;
using Npgsql;
using Xunit;

namespace MusicHub.Api.Tests.Data;

public class StartupMigrationTests
{
    private static Task Run(Func<CancellationToken, Task> attempt, List<TimeSpan> delays, CancellationToken ct = default) =>
        StartupMigration.RunAsync(attempt, (d, _) => { delays.Add(d); return Task.CompletedTask; },
            NullLogger.Instance, new Random(1), ct);

    private static NpgsqlException Unreachable() => new("down", new System.Net.Sockets.SocketException());

    [Fact]
    public async Task Succeeds_without_waiting_when_nothing_is_wrong()
    {
        var delays = new List<TimeSpan>();
        var calls = 0;

        await Run(_ => { calls++; return Task.CompletedTask; }, delays);

        Assert.Equal(1, calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Retries_an_unreachable_database_with_a_capped_backoff()
    {
        var delays = new List<TimeSpan>();
        var calls = 0;

        await Run(_ => ++calls <= 7 ? throw Unreachable() : Task.CompletedTask, delays);

        Assert.Equal(8, calls);
        double[] expected = [5, 10, 20, 40, 60, 60, 60];
        Assert.Equal(expected.Length, delays.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(delays[i].TotalSeconds, expected[i], expected[i] * 1.2);
        }
    }

    [Fact]
    public async Task A_failing_migration_is_not_retried()
    {
        var delays = new List<TimeSpan>();
        var calls = 0;
        var failure = new PostgresException("boom", "ERROR", "ERROR", "42P01");

        var thrown = await Assert.ThrowsAsync<PostgresException>(() =>
            Run(_ => { calls++; throw failure; }, delays));

        Assert.Same(failure, thrown);
        Assert.Equal(1, calls);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Stops_retrying_when_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var delays = new List<TimeSpan>();

        await Assert.ThrowsAsync<NpgsqlException>(() => Run(_ =>
        {
            cts.Cancel();
            throw Unreachable();
        }, delays, cts.Token));

        Assert.Empty(delays);
    }

    [Theory]
    [InlineData("57P03", true)]
    [InlineData("53300", true)]
    [InlineData("28P01", false)]
    [InlineData("42601", false)]
    public void Classifies_server_errors(string sqlState, bool unreachable) =>
        Assert.Equal(unreachable, StartupMigration.IsUnreachable(new PostgresException("x", "ERROR", "ERROR", sqlState)));

    [Fact]
    public async Task Readiness_is_unhealthy_until_the_migrations_are_applied()
    {
        var state = new MigrationState();
        var check = new MigrationHealthCheck(state);

        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, (await check.CheckHealthAsync(new())).Status);
        state.MarkComplete();
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy, (await check.CheckHealthAsync(new())).Status);
    }
}
