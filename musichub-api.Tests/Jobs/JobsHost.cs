using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MusicHub.Api.Data;
using MusicHub.Api.Jobs;

namespace MusicHub.Api.Tests.Jobs;

/// <summary>One "process": the real job services on a given storage, started and stopped like the app does.</summary>
internal sealed class JobsHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly HangfireServerService _server;

    private JobsHost(ServiceProvider provider, HangfireServerService server)
    {
        _provider = provider;
        _server = server;
    }

    public PingJobService Pings => _provider.GetRequiredService<PingJobService>();

    public JobEngine Engine => _provider.GetRequiredService<JobEngine>();

    public static async Task<JobsHost> StartAsync(IJobStorageFactory storageFactory, Action<HangfireOptions>? configure = null, bool migrated = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBackgroundJobs(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.Configure<HangfireOptions>(o => configure?.Invoke(o));
        services.AddSingleton(storageFactory);
        var migrations = new MigrationState();
        if (migrated)
            migrations.MarkComplete();
        services.AddSingleton(migrations);

        var provider = services.BuildServiceProvider();
        var server = provider.GetServices<IHostedService>().OfType<HangfireServerService>().Single();
        await server.StartAsync(CancellationToken.None);
        return new JobsHost(provider, server);
    }

    /// <summary>Waits until the job reaches <paramref name="state"/>; returns the last state seen.</summary>
    public async Task<string?> WaitForStateAsync(string jobId, string state)
    {
        string? current = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            current = await Pings.GetStateAsync(jobId, CancellationToken.None);
            if (current == state)
                break;
            await Task.Delay(100);
        }
        return current;
    }

    /// <summary>A graceful stop, as on SIGTERM: running jobs finish, the rest stays in storage.</summary>
    public Task StopAsync() => _server.StopAsync(CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _provider.DisposeAsync();
    }
}

internal sealed class FixedStorageFactory(JobStorage storage) : IJobStorageFactory
{
    public JobStorage Create() => storage;
}
