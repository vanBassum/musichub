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

    public IServiceProvider Services => _provider;

    public JobService Jobs => _provider.GetRequiredService<JobService>();

    public PingJobService Pings =>_provider.GetRequiredService<PingJobService>();

    public JobEngine Engine => _provider.GetRequiredService<JobEngine>();

    public static async Task<JobsHost> StartAsync(
        IJobStorageFactory storageFactory, Action<HangfireOptions>? configure = null, bool migrated = true, Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBackgroundJobs(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.Configure<HangfireOptions>(o => configure?.Invoke(o));
        services.AddSingleton(storageFactory);
        configureServices?.Invoke(services);
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
    public Task<string?> WaitForStateAsync(string jobId, string state) =>
        WaitUntilAsync(() => Pings.GetStateAsync(jobId, CancellationToken.None), current => current == state);

    /// <summary>Polls <paramref name="read"/> for up to 30 s until <paramref name="done"/> accepts the value; returns the last value read.</summary>
    public static async Task<T> WaitUntilAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var current = await read();
        while (!done(current) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
            current = await read();
        }
        return current;
    }

    /// <summary>A graceful stop, as on SIGTERM: running jobs finish, the rest stays in storage.</summary>
    public Task StopAsync() => _server.StopAsync(CancellationToken.None);

    /// <summary>Waits for the engine to be published, then stops the server: the engine stays usable, no worker runs.</summary>
    public async Task StopWhenReadyAsync()
    {
        await Engine.ClientAsync(CancellationToken.None);
        await StopAsync();
    }

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
