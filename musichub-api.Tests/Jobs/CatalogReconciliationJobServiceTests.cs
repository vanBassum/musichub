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
        (await JobsHost.WaitUntilAsync(() => host.Jobs.GetAsync(id, CancellationToken.None), done))!;

    [Fact]
    public async Task StartedRun_Succeeds_WithSummaryAndHistory()
    {
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()), configureServices: s => WithCatalog(s, nameof(StartedRun_Succeeds_WithSummaryAndHistory)));

        var id = await host.Jobs.TryStartAsync(JobKinds.CatalogReconciliation, CancellationToken.None);

        Assert.NotNull(id);
        var details = await WaitForAsync(host, id, d => d?.Execution.State == "Succeeded");
        Assert.Equal("Succeeded", details.Execution.State);
        Assert.Equal(0, (details.Execution.Summary as MusicHub.Api.Catalog.CatalogReconciliationSummary)?.Examined);
        Assert.NotNull(details.Execution.StartedAt);
        Assert.NotNull(details.Execution.FinishedAt);
        Assert.Null(details.Execution.Error);
        Assert.Contains("Processing", details.History.Select(h => h.State));
        Assert.Equal("Succeeded", details.History[^1].State);
        Assert.Equal(id, Assert.Single((await host.Jobs.ListAsync(10, CancellationToken.None)).Items).Id);
        Assert.Null(await host.Jobs.ActiveExecutionIdAsync(JobKinds.CatalogReconciliation, CancellationToken.None));
    }

    private sealed class UnresolvedIdentifier : IRecordingIdentifier
    {
        public Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct) =>
            Task.FromResult(IdentificationResult.Unresolved(
                "No identification tool produced a candidate.", [],
                [new IdentificationMethodResult("musicbrainz", IdentificationMethodStatus.NothingFound, "No match.", [])]));
    }

    [Fact]
    public async Task StartedRun_RecordsItsPerFileOutcomesUnderItsOwnExecutionId()
    {
        const string database = nameof(StartedRun_RecordsItsPerFileOutcomesUnderItsOwnExecutionId);
        var storage = new FakeFileStorage();
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()),
            configureServices: s =>
            {
                WithCatalog(s, database);
                s.AddSingleton<IFileStorage>(storage);
                s.AddSingleton<IRecordingIdentifier>(new UnresolvedIdentifier());
            });
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            var record = new MusicHub.Api.Ingestion.FileRecord
            {
                OriginalFilename = "x.mp3", StorageLocation = "default", StorageKey = "library/aa/x",
                Status = MusicHub.Api.Ingestion.FileStatus.Stored, Source = "upload", StartedAt = DateTimeOffset.UtcNow,
            };
            db.Files.Add(record);
            await db.SaveChangesAsync();
            await storage.PutAsync(record.StorageKey, new MemoryStream([1]), null, CancellationToken.None);
        }

        var id = await host.Jobs.TryStartAsync(JobKinds.CatalogReconciliation, CancellationToken.None);
        await WaitForAsync(host, id!, d => d?.Execution.State == "Succeeded");

        using var read = host.Services.CreateScope();
        var controller = new JobsController(host.Jobs, read.ServiceProvider.GetRequiredService<MusicHubDbContext>(), host.Services);
        var outcomes = Assert.IsAssignableFrom<IEnumerable<JobsController.FileOutcome>>(
            Assert.IsType<OkObjectResult>(await controller.Outcomes(id!, CancellationToken.None)).Value);
        var outcome = Assert.Single(outcomes);
        Assert.Equal("x.mp3", outcome.FileName);
        Assert.Equal(MusicHub.Api.Catalog.IdentificationDecision.NoMatch, outcome.Decision);
        Assert.Equal("musicbrainz", Assert.Single(outcome.Methods).Source);
        Assert.IsType<NotFoundResult>(await controller.Outcomes("999999", CancellationToken.None));
    }

    [Fact]
    public async Task StartedRun_ExaminesLinkedFilesOnlyWhenReIdentifyAllIsPassedThrough()
    {
        const string database = nameof(StartedRun_ExaminesLinkedFilesOnlyWhenReIdentifyAllIsPassedThrough);
        var storage = new FakeFileStorage();
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()),
            configureServices: s =>
            {
                WithCatalog(s, database);
                s.AddSingleton<IFileStorage>(storage);
                s.AddSingleton<IRecordingIdentifier>(new UnresolvedIdentifier());
            });
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MusicHubDbContext>();
            var track = new MusicHub.Api.Catalog.Track { Title = "Song", CreatedAt = DateTimeOffset.UtcNow };
            db.Tracks.Add(track);
            db.Files.Add(new MusicHub.Api.Ingestion.FileRecord
            {
                OriginalFilename = "x.mp3", StorageLocation = "default", StorageKey = "library/aa/x", Track = track,
                Status = MusicHub.Api.Ingestion.FileStatus.Stored, Source = "upload", StartedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            await storage.PutAsync("library/aa/x", new MemoryStream([1]), null, CancellationToken.None);
        }

        var normal = await host.Jobs.TryStartAsync(JobKinds.CatalogReconciliation, CancellationToken.None);
        var normalRun = await WaitForAsync(host, normal!, d => d?.Execution.State == "Succeeded");
        var all = await host.Jobs.TryStartAsync(
            JobKinds.CatalogReconciliation, CancellationToken.None, new JobStartOptions(ReIdentifyAll: true));
        var allRun = await WaitForAsync(host, all!, d => d?.Execution.State == "Succeeded");

        Assert.Equal(0, (normalRun.Execution.Summary as MusicHub.Api.Catalog.CatalogReconciliationSummary)?.Examined);
        var summary = Assert.IsType<MusicHub.Api.Catalog.CatalogReconciliationSummary>(allRun.Execution.Summary);
        Assert.Equal((1, 1), (summary.Examined, summary.Retained));
    }

    [Fact]
    public async Task Start_WhileARunIsWaiting_IsRefused()
    {
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()), configureServices: s => WithCatalog(s, nameof(Start_WhileARunIsWaiting_IsRefused)));
        // No server takes the job: it stays Enqueued.
        await host.StopWhenReadyAsync();

        var first = await host.Jobs.TryStartAsync(JobKinds.CatalogReconciliation, CancellationToken.None);
        var second = await host.Jobs.TryStartAsync(JobKinds.CatalogReconciliation, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(first, await host.Jobs.ActiveExecutionIdAsync(JobKinds.CatalogReconciliation, CancellationToken.None));
        var execution = Assert.Single((await host.Jobs.ListAsync(10, CancellationToken.None)).Items);
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

        var id = await host.Jobs.TryStartAsync(JobKinds.CatalogReconciliation, CancellationToken.None);

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

        Assert.Null(await host.Jobs.GetAsync(ping, CancellationToken.None));
        Assert.Null(await host.Jobs.GetAsync("999999", CancellationToken.None));
        var page = await host.Jobs.ListAsync(10, CancellationToken.None);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Controller_StartsThenRefusesAndReportsNotFound()
    {
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()), configureServices: s => WithCatalog(s, nameof(Controller_StartsThenRefusesAndReportsNotFound)));
        await host.StopWhenReadyAsync();
        var controller = new JobsController(host.Jobs, null!, host.Services)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var startedResult = await controller.StartCatalogReconciliation(CancellationToken.None);
        var started = Assert.IsType<AcceptedResult>(startedResult);
        var again = Assert.IsType<ObjectResult>(await controller.StartCatalogReconciliation(CancellationToken.None));
        var list = Assert.IsType<OkObjectResult>(await controller.List(CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, again.StatusCode);
        Assert.Equal(StatusCodes.Status404NotFound, ((NotFoundResult)await controller.Execution("999999", CancellationToken.None)).StatusCode);
        var tasks = Assert.IsType<JobsController.JobInfo[]>(list.Value);
        Assert.Equal(["catalog-reconciliation", "missing-music-acquisition"], tasks.Select(t => t.Id));
        Assert.NotNull(tasks[0].ActiveExecutionId);
        Assert.Null(tasks[1].ActiveExecutionId);
        Assert.NotNull(started.Value);
    }

    [Fact]
    public async Task Controller_WhenTheEngineIsNotReady_Returns503()
    {
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(new InMemoryStorage()), migrated: false, configureServices: s => WithCatalog(s, nameof(Controller_WhenTheEngineIsNotReady_Returns503)));
        var controller = new JobsController(host.Jobs, null!, host.Services)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        foreach (var result in new[]
        {
            await controller.List(CancellationToken.None),
            await controller.Executions(cancellationToken: CancellationToken.None),
            await controller.StartCatalogReconciliation(CancellationToken.None),
        })
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<ObjectResult>(result).StatusCode);
    }
}
