using Hangfire;
using Hangfire.Storage;
using Microsoft.Extensions.Options;

namespace MusicHub.Api.Jobs;

/// <summary>
/// The recurring schedule of a task as the Jobs area shows it. <paramref name="Enabled"/> is true when the schedule is
/// registered with Hangfire; <paramref name="Problem"/> says why a configured schedule is not.
/// </summary>
public sealed record JobScheduleInfo(bool Enabled, string? Cron, DateTime? NextRun, DateTime? LastRun, string? Problem);

/// <summary>
/// Registers the recurring run of the catalog reconciliation task with Hangfire, as <c>Hangfire:CatalogReconciliationCron</c>
/// says (off when it is empty), and reads its next run back from Hangfire. The recurring Hangfire job only asks
/// <see cref="JobService.TryStartAsync"/> for a run, so a scheduled run is skipped while another run is active, like
/// a manual start is refused. A cron expression Hangfire rejects disables the schedule and is reported, never stops the app.
/// </summary>
public sealed class JobSchedule(IOptions<HangfireOptions> options, ILogger<JobSchedule> logger)
{
    public const string RecurringJobId = "catalog-reconciliation";

    private volatile string? _problem;

    /// <summary>Makes Hangfire's recurring jobs match the configuration: registers the schedule, or removes one that is no longer configured.</summary>
    internal void Apply(JobStorage storage)
    {
        var cron = Cron();
        var manager = new RecurringJobManager(storage);
        try
        {
            if (cron is null)
            {
                manager.RemoveIfExists(RecurringJobId);
                _problem = null;
                return;
            }

            manager.AddOrUpdate<ScheduledJobRun>(
                RecurringJobId, run => run.Run(JobKinds.CatalogReconciliation.Id, CancellationToken.None), cron);
            _problem = null;
            logger.LogInformation("{Task} is scheduled ({Cron}, UTC).", JobKinds.CatalogReconciliation.Name, cron);
        }
        catch (Exception ex)
        {
            // Only the exception type is logged for a storage failure: its text can hold connection details.
            _problem = $"The schedule \"{cron}\" could not be registered ({ex.GetType().Name}). " +
                $"Set {HangfireOptions.SectionName}:{nameof(HangfireOptions.CatalogReconciliationCron)} to a five-field cron expression, or leave it empty.";
            logger.LogError("{Problem}", _problem);
        }
    }

    /// <summary>The schedule of <paramref name="kind"/>; null for a task that cannot be scheduled.</summary>
    public JobScheduleInfo? Read(JobKind kind, JobStorage storage)
    {
        if (kind != JobKinds.CatalogReconciliation)
            return null;

        var cron = Cron();
        if (cron is null || _problem is not null)
            return new JobScheduleInfo(false, cron, null, null, _problem);

        using var connection = storage.GetConnection();
        var recurring = connection.GetRecurringJobs([RecurringJobId]).FirstOrDefault();
        return new JobScheduleInfo(true, cron, recurring?.NextExecution, recurring?.LastExecution, recurring?.Error);
    }

    // The configured expression, or null when the task is not scheduled.
    private string? Cron() =>
        string.IsNullOrWhiteSpace(options.Value.CatalogReconciliationCron) ? null : options.Value.CatalogReconciliationCron.Trim();
}

/// <summary>The Hangfire job a recurring schedule runs: it starts the task unless a run of it is already active.</summary>
public class ScheduledJobRun(JobService jobs)
{
    public async Task Run(string kindId, CancellationToken cancellationToken)
    {
        var kind = JobKinds.All.Single(k => k.Id == kindId);
        await jobs.TryStartAsync(kind, cancellationToken);
    }
}
