using Hangfire.InMemory;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;
using MusicHub.Api.Jobs;
using MusicHub.Api.Storage;
using MusicHub.Api.Tests.Ingestion;

namespace MusicHub.Api.Tests.Jobs;

public class CatalogReconciliationJobServiceTests
{
    private sealed class NeverIdentifier : IRecordingIdentifier
    {
        public Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct) =>
            throw new InvalidOperationException("No file should be examined.");
    }

    private static void WithCatalog(IServiceCollection services, string database)
    {
        services.AddDbContext<MusicHubDbContext>(o => o.UseInMemoryDatabase(database));
        services.AddSingleton<IFileStorage>(new FakeFileStorage());
        services.AddSingleton<IRecordingIdentifier>(new NeverIdentifier());
        services.AddSingleton(TimeProvider.System);
    }

    private static async Task<JobExecutionDetails> WaitForAsync(JobsHost host, string id, Func<JobExecutionDetails?, bool> done) =>
        (await JobsHost.WaitUntilAsync(() => host.Catalog.GetAsync(id, CancellationToken.None), done))!;

    [Fact]
    public async Task StartedRun_Succeeds_WithSummaryAndHistory()
    {
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()), configureServices: s => WithCatalog(s, nameof(StartedRun_Succeeds_WithSummaryAndHistory)));

        var id = await host.Catalog.TryStartAsync(CancellationToken.None);

        Assert.NotNull(id);
        var details = await WaitForAsync(host, id, d => d?.Execution.State == "Succeeded");
        Assert.Equal("Succeeded", details.Execution.State);
        Assert.Equal(0, details.Execution.Summary?.Examined);
        Assert.NotNull(details.Execution.StartedAt);
        Assert.NotNull(details.Execution.FinishedAt);
        Assert.Null(details.Execution.Error);
        Assert.Contains("Processing", details.History.Select(h => h.State));
        Assert.Equal("Succeeded", details.History[^1].State);
        Assert.Equal(id, Assert.Single(await host.Catalog.ListAsync(10, CancellationToken.None)).Id);
        Assert.Null(await host.Catalog.ActiveExecutionIdAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Start_WhileARunIsWaiting_IsRefused()
    {
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()), configureServices: s => WithCatalog(s, nameof(Start_WhileARunIsWaiting_IsRefused)));
        // No server takes the job: it stays Enqueued.
        await host.StopWhenReadyAsync();

        var first = await host.Catalog.TryStartAsync(CancellationToken.None);
        var second = await host.Catalog.TryStartAsync(CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(first, await host.Catalog.ActiveExecutionIdAsync(CancellationToken.None));
        var execution = Assert.Single(await host.Catalog.ListAsync(10, CancellationToken.None));
        Assert.Equal("Enqueued", execution.State);
        Assert.Null(execution.StartedAt);
    }

    [Fact]
    public async Task FailedRun_ShowsItsErrorWithoutAStackTrace()
    {
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()),
            o => o.RetryAttempts = 0,
            configureServices: s =>
            {
                s.AddDbContext<MusicHubDbContext>(o => o.UseSqlite("Data Source=/nonexistent-dir/musichub.db"));
                s.AddSingleton<IFileStorage>(new FakeFileStorage());
                s.AddSingleton<IRecordingIdentifier>(new NeverIdentifier());
                s.AddSingleton(TimeProvider.System);
            });

        var id = await host.Catalog.TryStartAsync(CancellationToken.None);

        // Hangfire's retry policy is process-wide, so the run is either Failed or waiting for a retry.
        var details = await WaitForAsync(host, id!, d => d?.Execution.Error is not null);
        Assert.Contains(details.Execution.State, new[] { "Failed", "Scheduled" });
        Assert.False(string.IsNullOrWhiteSpace(details.Execution.Error));
        Assert.NotNull(details.ErrorType);
        Assert.Null(details.Execution.Summary);
    }

    [Fact]
    public async Task UnknownOrOtherJobs_AreNotExecutions()
    {
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()), configureServices: s => WithCatalog(s, nameof(UnknownOrOtherJobs_AreNotExecutions)));
        var ping = await host.Pings.EnqueueAsync(CancellationToken.None);
        await host.WaitForStateAsync(ping, "Succeeded");

        Assert.Null(await host.Catalog.GetAsync(ping, CancellationToken.None));
        Assert.Null(await host.Catalog.GetAsync("999999", CancellationToken.None));
        Assert.Empty(await host.Catalog.ListAsync(10, CancellationToken.None));
    }

    [Fact]
    public async Task Controller_StartsThenRefusesAndReportsNotFound()
    {
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()), configureServices: s => WithCatalog(s, nameof(Controller_StartsThenRefusesAndReportsNotFound)));
        await host.StopWhenReadyAsync();
        var controller = new JobsController(host.Catalog)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var startedResult = await controller.StartCatalogReconciliation(CancellationToken.None);
        var started = Assert.IsType<AcceptedResult>(startedResult);
        var again = Assert.IsType<ObjectResult>(await controller.StartCatalogReconciliation(CancellationToken.None));
        var list = Assert.IsType<OkObjectResult>(await controller.List(CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, again.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, ((NotFoundResult)await controller.Execution("999999", CancellationToken.None)).StatusCode);
        var info = Assert.Single(Assert.IsType<JobsController.JobInfo[]>(list.Value));
        Assert.Equal("catalog-reconciliation", info.Id);
        Assert.NotNull(info.ActiveExecutionId);
        Assert.NotNull(started.Value);
    }

    [Fact]
    public async Task Controller_WhenTheEngineIsNotReady_Returns503()
    {
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()), migrated: false, configureServices: s => WithCatalog(s, nameof(Controller_WhenTheEngineIsNotReady_Returns503)));
        var controller = new JobsController(host.Catalog)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        foreach (var result in new[]
        {
            await controller.List(CancellationToken.None),
            await controller.Executions(CancellationToken.None),
            await controller.StartCatalogReconciliation(CancellationToken.None),
        })
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
    }
}
