using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;

namespace MusicHub.Api.Jobs;

/// <summary>One run of a task, read from Hangfire. <paramref name="State"/> is Hangfire's own state name.</summary>
/// <param name="Summary">What the run did, as its kind's summary type; set once it Succeeded.</param>
/// <param name="Error">The message of the last failure (a failed run, or the attempt that is being retried).</param>
/// <param name="Kind">The <see cref="JobKind.Id"/> of the task this is a run of.</param>
/// <param name="Total">How many items the run set out to go through, when its job reports it while running.</param>
/// <param name="Stuck">Whether the run has been enqueued or processing for longer than <see cref="JobService.StuckAfter"/>.</param>
public sealed record JobExecution(
    string Id,
    string State,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    object? Summary,
    string? Error,
    string Kind = "",
    int? Total = null,
    bool Stuck = false);

/// <summary>Which executions the history lists: all of them, the ones that ended Failed, or the ones that look stuck.</summary>
public enum JobHistoryFilter
{
    All,
    Failed,
    Stuck,
}

/// <summary>One page of the history; <paramref name="Total"/> counts every execution that matches the filter, not only this page.</summary>
public sealed record JobExecutionPage(IReadOnlyList<JobExecution> Items, int Total);

/// <summary>One step of an execution's Hangfire state history, oldest first.</summary>
public sealed record JobStateEntry(string State, DateTime At, string? Reason);

public sealed record JobExecutionDetails(JobExecution Execution, IReadOnlyList<JobStateEntry> History, string? ErrorType);

/// <summary>
/// Starts the tasks of <see cref="JobKinds"/> and reads their executions from Hangfire (state history, the summary the
/// job returned, the failure message). It is the only way the API reaches the jobs; there is no Hangfire dashboard. A
/// run of a task is refused while another run of it is enqueued, scheduled for a retry, or processing.
/// </summary>
public sealed class JobService(JobEngine engine, JobSchedule schedule, TimeProvider? time = null)
{
    private const string Queue = "default";
    private const int StatePageSize = 500;

    /// <summary>How long a run may wait in the queue or process before the history calls it stuck.</summary>
    public static readonly TimeSpan StuckAfter = TimeSpan.FromHours(1);
    private static readonly string[] FinalStates = ["Succeeded", "Failed", "Deleted"];
    private static readonly SemaphoreSlim StartLock = new(1, 1);

    public bool IsReady => engine.IsReady;

    /// <summary>Enqueues a run of <paramref name="kind"/> unless one is active. Returns the new execution id, or null when a run is already active.</summary>
    /// <param name="options">What the start chooses; null for the defaults, as a scheduled run starts.</param>
    public async Task<string?> TryStartAsync(JobKind kind, CancellationToken cancellationToken, JobStartOptions? options = null)
    {
        await StartLock.WaitAsync(cancellationToken);
        try
        {
            if (await ActiveExecutionIdAsync(kind, cancellationToken) is not null)
                return null;
            return kind.Enqueue(await engine.ClientAsync(cancellationToken), options ?? new JobStartOptions());
        }
        finally
        {
            StartLock.Release();
        }
    }

    /// <summary>Enqueues the Search for Missing Music run for one track and returns its execution id.</summary>
    public async Task<string> EnqueueTrackSearchAsync(int trackId, CancellationToken cancellationToken) =>
        (await engine.ClientAsync(cancellationToken))
            .Enqueue<MissingMusicAcquisitionJob>(job => job.RunForTrack(trackId, null, CancellationToken.None));

    /// <summary>The id of the run of <paramref name="kind"/> that is enqueued, waiting for a retry or processing, or null when none is.</summary>
    public async Task<string?> ActiveExecutionIdAsync(JobKind kind, CancellationToken cancellationToken)
    {
        var monitor = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi();
        return ActiveIds(monitor, kind).OrderByDescending(IdOrder).FirstOrDefault();
    }

    /// <summary>The recurring schedule of <paramref name="kind"/>, with its next run; null for a task that cannot be scheduled.</summary>
    public async Task<JobScheduleInfo?> ScheduleAsync(JobKind kind, CancellationToken cancellationToken) =>
        schedule.Read(kind, await engine.StorageAsync(cancellationToken));

    // An active run is enqueued, scheduled for a retry, or processing. Without a kind: of any task.
    private static IEnumerable<string> ActiveIds(IMonitoringApi monitor, JobKind? kind = null) =>
        AllIds((from, count) => monitor.EnqueuedJobs(Queue, from, count), j => j.Job, kind)
            .Concat(AllIds(monitor.ProcessingJobs, j => j.Job, kind))
            .Concat(AllIds(monitor.ScheduledJobs, j => j.Job, kind));

    private static IEnumerable<string> FailedIds(IMonitoringApi monitor) =>
        AllIds(monitor.FailedJobs, j => j.Job);

    private static IEnumerable<string> SucceededIds(IMonitoringApi monitor) =>
        AllIds(monitor.SucceededJobs, j => j.Job);

    /// <summary>
    /// One page of the executions of every task, newest first. <see cref="JobExecutionPage.Total"/> is the number of
    /// executions Hangfire still holds that match <paramref name="filter"/>.
    /// </summary>
    public async Task<JobExecutionPage> ListAsync(
        int take, CancellationToken cancellationToken, JobHistoryFilter filter = JobHistoryFilter.All, int skip = 0)
    {
        var monitor = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi();
        if (filter == JobHistoryFilter.Stuck)
        {
            var stuck = Details(monitor, ActiveIds(monitor).Distinct())
                .Select(details => details.Execution)
                .Where(execution => execution.Stuck)
                .OrderByDescending(execution => IdOrder(execution.Id))
                .ToList();
            return new JobExecutionPage(stuck.Skip(skip).Take(take).ToList(), stuck.Count);
        }

        var ids = (filter == JobHistoryFilter.Failed
                ? FailedIds(monitor)
                : ActiveIds(monitor).Concat(SucceededIds(monitor)).Concat(FailedIds(monitor)))
            .Distinct()
            .OrderByDescending(IdOrder)
            .ToList();
        var items = Details(monitor, ids.Skip(skip).Take(take)).Select(details => details.Execution).ToList();
        return new JobExecutionPage(items, ids.Count);
    }

    /// <summary>The newest executions of every task that ended Failed, newest first.</summary>
    public async Task<IReadOnlyList<JobExecutionDetails>> FailedAsync(int count, CancellationToken cancellationToken)
    {
        var monitor = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi();
        return Details(monitor, FailedIds(monitor).OrderByDescending(IdOrder).Take(count)).ToList();
    }

    public async Task<JobExecutionDetails?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var details = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi().JobDetails(id);
        return JobKinds.Of(details?.Job?.Type) is { } kind ? ToDetails(id, details!, kind, StuckBefore()) : null;
    }

    // The jobs of one kind, or of any kind when none is given.
    private static IEnumerable<string> Ids<T>(IEnumerable<KeyValuePair<string, T>> jobs, Func<T, Job?> job, JobKind? kind = null) =>
        jobs.Where(pair => kind is null
                ? JobKinds.Of(job(pair.Value)?.Type) is not null
                : job(pair.Value)?.Type == kind.JobType)
            .Select(pair => pair.Key);

    // Every job of a state: Hangfire lists a state page by page, until a page comes back short.
    private static IEnumerable<string> AllIds<T>(
        Func<int, int, IEnumerable<KeyValuePair<string, T>>> page, Func<T, Job?> job, JobKind? kind = null)
    {
        for (var from = 0; ; from += StatePageSize)
        {
            var jobs = page(from, StatePageSize).ToList();
            foreach (var id in Ids(jobs, job, kind))
                yield return id;
            if (jobs.Count < StatePageSize)
                yield break;
        }
    }

    private DateTime StuckBefore() => (time ?? TimeProvider.System).GetUtcNow().UtcDateTime - StuckAfter;

    // The executions of the given jobs, in the order given; a job that expired or is not one of ours is left out.
    private IEnumerable<JobExecutionDetails> Details(IMonitoringApi monitor, IEnumerable<string> ids)
    {
        var stuckBefore = StuckBefore();
        return ids.Select(id => (Id: id, Details: monitor.JobDetails(id)))
            .Where(job => job.Details is not null && JobKinds.Of(job.Details.Job?.Type) is not null)
            .Select(job => ToDetails(job.Id, job.Details!, JobKinds.Of(job.Details!.Job?.Type)!, stuckBefore));
    }

    // Hangfire job ids are increasing numbers, so the biggest is the newest.
    private static long IdOrder(string id) => long.TryParse(id, out var n) ? n : 0;

    private static JobExecutionDetails ToDetails(string id, JobDetailsDto details, JobKind kind, DateTime stuckBefore)
    {
        var history = details.History
            .OrderBy(h => h.CreatedAt)
            .Select(h => new JobStateEntry(h.StateName, h.CreatedAt, h.Reason))
            .ToList();
        var last = details.History.OrderBy(h => h.CreatedAt).Last();
        var processing = details.History.Where(h => h.StateName == "Processing").MaxBy(h => h.CreatedAt);
        var succeeded = details.History.FirstOrDefault(h => h.StateName == "Succeeded");
        // A run that failed, was retried and then Succeeded reports no error.
        var failed = last.StateName == "Succeeded"
            ? null
            : details.History.Where(h => h.Data.ContainsKey("ExceptionMessage")).MaxBy(h => h.CreatedAt);
        var finished = FinalStates.Contains(last.StateName);

        var execution = new JobExecution(
            Id: id,
            State: last.StateName,
            CreatedAt: details.CreatedAt ?? history[0].At,
            StartedAt: processing?.CreatedAt,
            FinishedAt: finished ? last.CreatedAt : null,
            Summary: succeeded is null ? null : ReadSummary(succeeded, kind),
            Error: failed?.Data["ExceptionMessage"],
            Kind: kind.Id,
            Total: details.Properties.TryGetValue(JobProgress.TotalParameter, out var total) && int.TryParse(total, out var count)
                ? count
                : null,
            Stuck: last.StateName is "Enqueued" or "Processing" && last.CreatedAt < stuckBefore);
        return new JobExecutionDetails(execution, history, failed is not null && failed.Data.TryGetValue("ExceptionType", out var type) ? type : null);
    }

    private static object? ReadSummary(StateHistoryDto succeeded, JobKind kind)
    {
        if (!succeeded.Data.TryGetValue("Result", out var json) || string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize(json, kind.SummaryType, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
