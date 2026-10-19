using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.Options;
using MusicHub.Api.Jobs;
using Npgsql;

namespace MusicHub.Api.Tests.Jobs;

public class HangfireJobsTests
{
    [Fact]
    public async Task EnqueuedPingJob_IsExecuted()
    {
        await using var host = await JobsHost.StartAsync(new FixedStorageFactory(new InMemoryStorage()));

        var id = await host.Pings.EnqueueAsync(CancellationToken.None);

        Assert.Equal("Succeeded", await host.WaitForStateAsync(id, "Succeeded"));
    }

    [Fact]
    public async Task JobEnqueuedWhileNoServerRuns_IsExecutedAfterTheRestart()
    {
        // The storage stands for the database: it outlives the "process".
        var storage = new InMemoryStorage();
        await using (var first = await JobsHost.StartAsync(new FixedStorageFactory(storage)))
            await first.StopAsync();

        var id = new BackgroundJobClient(storage).Enqueue<PingJob>(job => job.Run());
        Assert.Equal("Enqueued", storage.GetMonitoringApi().JobDetails(id).History.First().StateName);

        await using var second = await JobsHost.StartAsync(new FixedStorageFactory(storage));

        Assert.Equal("Succeeded", await second.WaitForStateAsync(id, "Succeeded"));
    }

    [Fact]
    public async Task Server_WaitsForTheMigrations()
    {
        await using var host = await JobsHost.StartAsync(new FixedStorageFactory(new InMemoryStorage()), migrated: false);

        await Task.Delay(1500);

        Assert.False(host.Engine.IsReady);
    }

    [Fact]
    public async Task Configuration_SetsWorkersAndRetries()
    {
        var storage = new InMemoryStorage();
        await using var host = await JobsHost.StartAsync(new FixedStorageFactory(storage), o =>
        {
            o.WorkerCount = 3;
            o.RetryAttempts = 5;
        });
        await host.Engine.ClientAsync(CancellationToken.None);

        var retry = GlobalJobFilters.Filters.Select(f => f.Instance).OfType<AutomaticRetryAttribute>().Single();
        Assert.Equal(5, retry.Attempts);
        var server = storage.GetMonitoringApi().Servers().Single();
        Assert.Equal(3, server.WorkersCount);
    }

    [Fact]
    public void Defaults_AreBounded()
    {
        var options = new HangfireOptions();

        Assert.Null(options.Validate());
        Assert.Equal(2, options.WorkerCount);
        Assert.Equal(3, options.RetryAttempts);
    }

    [Theory]
    [InlineData(0, 3, 1, 15)]
    [InlineData(33, 3, 1, 15)]
    [InlineData(2, -1, 1, 15)]
    [InlineData(2, 21, 1, 15)]
    [InlineData(2, 3, 0, 15)]
    [InlineData(2, 3, 1, 0)]
    [InlineData(2, 3, 1, 26)]
    public void Validate_RejectsUnboundedSettings(int workers, int retries, int retentionHours, int shutdownSeconds)
    {
        var options = new HangfireOptions
        {
            WorkerCount = workers,
            RetryAttempts = retries,
            RetentionHours = retentionHours,
            ShutdownTimeoutSeconds = shutdownSeconds,
        };

        Assert.NotNull(options.Validate());
    }

    [Fact]
    public async Task InvalidSettings_DisableTheEngineWithoutStoppingTheApp()
    {
        await using var host = await JobsHost.StartAsync(new FixedStorageFactory(new InMemoryStorage()), o => o.WorkerCount = 0);

        await Task.Delay(500);

        Assert.False(host.Engine.IsReady);
    }

    [PostgresFact]
    public async Task PostgreSql_JobSurvivesARestart()
    {
        var schema = $"hangfire_test_{Guid.NewGuid():N}";
        try
        {
            string id;
            // The first process creates the schema, enqueues, and stops before any worker could take the job.
            var factory = new PostgreSqlJobStorageFactory(
                Options.Create(PostgresFactAttribute.Database()), Options.Create(new HangfireOptions { SchemaName = schema }));
            await using (var first = await JobsHost.StartAsync(factory, o => o.SchemaName = schema))
            {
                await first.Engine.ClientAsync(CancellationToken.None);
                await first.StopAsync();
                id = new BackgroundJobClient(factory.Create()).Enqueue<PingJob>(job => job.Run());
            }

            // A new process on the same database finds the job and runs it.
            await using var second = await JobsHost.StartAsync(factory, o => o.SchemaName = schema);

            Assert.Equal("Succeeded", await second.WaitForStateAsync(id, "Succeeded"));
        }
        finally
        {
            await using var connection = new NpgsqlConnection(PostgresFactAttribute.ConnectionString);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
