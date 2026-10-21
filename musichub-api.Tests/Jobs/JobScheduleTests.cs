using Hangfire.InMemory;
using Hangfire.Storage;
using MusicHub.Api.Jobs;

namespace MusicHub.Api.Tests.Jobs;

public class JobScheduleTests
{
    private static readonly JobKind Reconciliation = JobKinds.CatalogReconciliation;

    private static async Task<JobScheduleInfo> ScheduleOfAsync(JobsHost host)
    {
        await host.Engine.ClientAsync(CancellationToken.None);
        return (await host.Jobs.ScheduleAsync(Reconciliation, CancellationToken.None))!;
    }

    [Fact]
    public async Task WithoutACron_TheTaskIsNotScheduled()
    {
        var storage = new InMemoryStorage();
        await using var host = await JobsHost.StartAsync(new FixedStorageFactory(storage));

        var schedule = await ScheduleOfAsync(host);

        Assert.False(schedule.Enabled);
        Assert.Null(schedule.Cron);
        Assert.Null(schedule.NextRun);
        Assert.Null(schedule.Problem);
        Assert.DoesNotContain(storage.GetConnection().GetRecurringJobs([JobSchedule.RecurringJobId]), j => j.Cron is not null);
    }

    [Fact]
    public async Task WithACron_TheTaskIsRegisteredWithItsNextRun()
    {
        var storage = new InMemoryStorage();
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(storage), o => o.CatalogReconciliationCron = " 0 3 * * * ");

        var schedule = await ScheduleOfAsync(host);

        Assert.True(schedule.Enabled);
        Assert.Equal("0 3 * * *", schedule.Cron);
        Assert.Null(schedule.Problem);
        var next = Assert.NotNull(schedule.NextRun);
        Assert.True(next > DateTime.UtcNow);
        Assert.Equal(new TimeSpan(3, 0, 0), next.TimeOfDay);
        Assert.Single(storage.GetConnection().GetRecurringJobs([JobSchedule.RecurringJobId]), j => j.Cron is not null);
    }

    [Fact]
    public async Task AnInvalidCron_LeavesTheTaskUnscheduledAndReportsWhy()
    {
        var storage = new InMemoryStorage();
        await using var host = await JobsHost.StartAsync(
            new FixedStorageFactory(storage), o => o.CatalogReconciliationCron = "every night");

        var schedule = await ScheduleOfAsync(host);

        Assert.True(host.Engine.IsReady);
        Assert.False(schedule.Enabled);
        Assert.Null(schedule.NextRun);
        Assert.Contains("Hangfire:CatalogReconciliationCron", schedule.Problem);
        Assert.DoesNotContain(storage.GetConnection().GetRecurringJobs([JobSchedule.RecurringJobId]), j => j.Cron is not null);
    }

    [Fact]
    public async Task RemovingTheCron_RemovesTheRegisteredSchedule()
    {
        var storage = new InMemoryStorage();
        await using (var scheduled = await JobsHost.StartAsync(
            new FixedStorageFactory(storage), o => o.CatalogReconciliationCron = "0 3 * * *"))
            await ScheduleOfAsync(scheduled);

        await using var unscheduled = await JobsHost.StartAsync(new FixedStorageFactory(storage));

        Assert.False((await ScheduleOfAsync(unscheduled)).Enabled);
        Assert.DoesNotContain(storage.GetConnection().GetRecurringJobs([JobSchedule.RecurringJobId]), j => j.Cron is not null);
    }

    [Fact]
    public async Task OtherTasks_HaveNoSchedule()
    {
        await using var host = await JobsHost.StartAsync(new FixedStorageFactory(new InMemoryStorage()));
        await host.Engine.ClientAsync(CancellationToken.None);

        Assert.Null(await host.Jobs.ScheduleAsync(JobKinds.MissingMusicAcquisition, CancellationToken.None));
    }

    [Fact]
    public async Task AScheduledRun_IsSkippedWhileARunIsActive()
    {
        await using var host = await JobsHost.StartAsync(new FixedStorageFactory(new InMemoryStorage()));
        // No worker runs, so the manual run stays enqueued.
        await host.StopWhenReadyAsync();
        var manual = await host.Jobs.TryStartAsync(Reconciliation, CancellationToken.None);

        await new ScheduledJobRun(host.Jobs).Run(Reconciliation.Id, CancellationToken.None);

        Assert.NotNull(manual);
        Assert.Equal(manual, await host.Jobs.ActiveExecutionIdAsync(Reconciliation, CancellationToken.None));
        Assert.Single((await host.Jobs.ListAsync(10, CancellationToken.None)).Items);
    }

    [Fact]
    public async Task AScheduledRun_StartsTheTaskWhenNoneIsActive()
    {
        await using var host = await JobsHost.StartAsync(new FixedStorageFactory(new InMemoryStorage()));
        await host.StopWhenReadyAsync();

        await new ScheduledJobRun(host.Jobs).Run(Reconciliation.Id, CancellationToken.None);

        Assert.NotNull(await host.Jobs.ActiveExecutionIdAsync(Reconciliation, CancellationToken.None));
    }
}
