using Hangfire;
using Hangfire.Common;
using Hangfire.InMemory;
using Hangfire.States;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Api.Controllers;
using MusicHub.Api.Jobs;

namespace MusicHub.Api.Tests.Jobs;

public class JobHistoryFilterTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // A host whose server is stopped, so jobs created in the storage stay in the state they were created in. The
    // client has no filters: the process-wide automatic retry would turn a created Failed state into a retry.
    private static async Task<(JobsHost Host, IBackgroundJobClient Client)> StoppedHostAsync()
    {
        var storage = new InMemoryStorage();
        var host = await JobsHost.StartAsync(new FixedStorageFactory(storage));
        await host.StopWhenReadyAsync();
        return (host, new BackgroundJobClient(storage, new JobFilterCollection()));
    }

    private static string Create(IBackgroundJobClient client, IState state) =>
        client.Create(Job.FromExpression<CatalogReconciliationJob>(job => job.Run(null, CancellationToken.None)), state);

    private static IState Failed(string message) => new FailedState(new InvalidOperationException(message));

    [Fact]
    public async Task FailedFilter_ListsOnlyFailedRuns_WithTheirErrorAndAMatchingTotal()
    {
        var (host, client) = await StoppedHostAsync();
        await using var _ = host;
        var failedOld = Create(client, Failed("first failure"));
        Create(client, new SucceededState(null, 0, 0));
        var failedNew = Create(client, Failed("second failure"));
        Create(client, new EnqueuedState());

        var failed = await host.Jobs.ListAsync(10, CancellationToken.None, JobHistoryFilter.Failed);
        var all = await host.Jobs.ListAsync(10, CancellationToken.None);

        Assert.Equal(2, failed.Total);
        Assert.Equal([failedNew, failedOld], failed.Items.Select(e => e.Id));
        Assert.All(failed.Items, e => Assert.Equal("Failed", e.State));
        Assert.Equal(["second failure", "first failure"], failed.Items.Select(e => e.Error));
        Assert.Equal(4, all.Total);
    }

    [Fact]
    public async Task Total_CountsEveryMatchingRun_NotOnlyThePage()
    {
        var (host, client) = await StoppedHostAsync();
        await using var _ = host;
        var ids = Enumerable.Range(0, 5).Select(i => Create(client, Failed($"failure {i}"))).ToList();

        var page = await host.Jobs.ListAsync(2, CancellationToken.None, JobHistoryFilter.Failed, skip: 2);

        Assert.Equal(5, page.Total);
        Assert.Equal([ids[2], ids[1]], page.Items.Select(e => e.Id));
    }

    [Fact]
    public async Task StuckFilter_ListsRunsWaitingOrProcessingForTooLong()
    {
        var (host, client) = await StoppedHostAsync();
        await using var _ = host;
        var waiting = Create(client, new EnqueuedState());
        Create(client, Failed("not stuck, failed"));
        var jobs = new JobService(host.Engine, host.Services.GetRequiredService<JobSchedule>(), new FixedTime(DateTimeOffset.UtcNow + JobService.StuckAfter + TimeSpan.FromMinutes(1)));

        var stuck = await jobs.ListAsync(10, CancellationToken.None, JobHistoryFilter.Stuck);
        var freshStuck = await host.Jobs.ListAsync(10, CancellationToken.None, JobHistoryFilter.Stuck);

        Assert.Equal(1, stuck.Total);
        var execution = Assert.Single(stuck.Items);
        Assert.Equal(waiting, execution.Id);
        Assert.True(execution.Stuck);
        Assert.Equal(0, freshStuck.Total);
        Assert.Empty(freshStuck.Items);
    }

    [Fact]
    public async Task Controller_ReturnsThePageWithItsTotal_AndClampsTheWindow()
    {
        var (host, client) = await StoppedHostAsync();
        await using var _ = host;
        Create(client, Failed("boom"));
        Create(client, new SucceededState(null, 0, 0));
        var controller = new JobsController(host.Jobs, null!, host.Services);

        var result = Assert.IsType<OkObjectResult>(await controller.Executions(JobHistoryFilter.Failed, skip: -5, take: 1000));

        var page = Assert.IsType<JobExecutionPage>(result.Value);
        Assert.Equal(1, page.Total);
        Assert.Equal("boom", Assert.Single(page.Items).Error);
    }
}
