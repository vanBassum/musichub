using System.Text.Json;
using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;

namespace MusicHub.Api.Jobs;

/// <summary>One run of a task, read from Hangfire. <paramref name="State"/> is Hangfire's own state name.</summary>
/// <param name="Summary">What the run did, as its kind's summary type; set once it Succeeded.</param>
/// <param name="Error">The message of the last failure (a failed run, or the attempt that is being retried).</param>
/// <param name="Kind">The <see cref="JobKind.Id"/> of the task this is a run of.</param>
/// <param name="Total">How many items the run set out to go through, when its job reports it while running.</param>
public sealed record JobExecution(
    string Id,
    string State,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    object? Summary,
    string? Error,
    string Kind = "",
    int? Total = null);

/// <summary>One step of an execution's Hangfire state history, oldest first.</summary>
public sealed record JobStateEntry(string State, DateTime At, string? Reason);

public sealed record JobExecutionDetails(JobExecution Execution, IReadOnlyList<JobStateEntry> History, string? ErrorType);

/// <summary>
/// Starts the tasks of <see cref="JobKinds"/> and reads their executions from Hangfire (state history, the summary the
/// job returned, the failure message). It is the only way the API reaches the jobs; there is no Hangfire dashboard. A
/// run of a task is refused while another run of it is enqueued, scheduled for a retry, or processing.
/// </summary>
public sealed class JobService(JobEngine engine)
{
    private const string Queue = "default";
    private const int PerState = 50;
    private static readonly string[] FinalStates = ["Succeeded", "Failed", "Deleted"];
    private static readonly SemaphoreSlim StartLock = new(1, 1);

    public bool IsReady => engine.IsReady;

    /// <summary>Enqueues a run of <paramref name="kind"/> unless one is active. Returns the new execution id, or null when a run is already active.</summary>
    public async Task<string?> TryStartAsync(JobKind kind, CancellationToken cancellationToken)
    {
        await StartLock.WaitAsync(cancellationToken);
        try
        {
            if (await ActiveExecutionIdAsync(kind, cancellationToken) is not null)
                return null;
            return kind.Enqueue(await engine.ClientAsync(cancellationToken));
        }
        finally
        {
            StartLock.Release();
        }
    }

    /// <summary>The id of the run of <paramref name="kind"/> that is enqueued, waiting for a retry or processing, or null when none is.</summary>
    public async Task<string?> ActiveExecutionIdAsync(JobKind kind, CancellationToken cancellationToken)
    {
        var monitor = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi();
        return ActiveIds(monitor, kind).OrderByDescending(IdOrder).FirstOrDefault();
    }

    // An active run is enqueued, scheduled for a retry, or processing. Without a kind: of any task.
    private static IEnumerable<string> ActiveIds(IMonitoringApi monitor, JobKind? kind = null) =>
        Ids(monitor.EnqueuedJobs(Queue, 0, PerState), j => j.Job, kind)
            .Concat(Ids(monitor.ProcessingJobs(0, PerState), j => j.Job, kind))
            .Concat(Ids(monitor.ScheduledJobs(0, PerState), j => j.Job, kind));

    /// <summary>The newest executions of every task, newest first.</summary>
    public async Task<IReadOnlyList<JobExecution>> ListAsync(int count, CancellationToken cancellationToken)
    {
        var monitor = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi();
        var ids = ActiveIds(monitor)
            .Concat(Ids(monitor.SucceededJobs(0, PerState), j => j.Job))
            .Concat(Ids(monitor.FailedJobs(0, PerState), j => j.Job))
            .Distinct();
        return NewestDetails(monitor, ids, count).Select(details => details.Execution).ToList();
    }

    /// <summary>The newest executions of every task that ended Failed, newest first.</summary>
    public async Task<IReadOnlyList<JobExecutionDetails>> FailedAsync(int count, CancellationToken cancellationToken)
    {
        var monitor = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi();
        return NewestDetails(monitor, Ids(monitor.FailedJobs(0, PerState), j => j.Job), count).ToList();
    }

    public async Task<JobExecutionDetails?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var details = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi().JobDetails(id);
        return JobKinds.Of(details?.Job?.Type) is { } kind ? ToDetails(id, details!, kind) : null;
    }

    // The jobs of one kind, or of any kind when none is given.
    private static IEnumerable<string> Ids<T>(IEnumerable<KeyValuePair<string, T>> jobs, Func<T, Job?> job, JobKind? kind = null) =>
        jobs.Where(pair => kind is null
                ? JobKinds.Of(job(pair.Value)?.Type) is not null
                : job(pair.Value)?.Type == kind.JobType)
            .Select(pair => pair.Key);

    private static IEnumerable<JobExecutionDetails> NewestDetails(IMonitoringApi monitor, IEnumerable<string> ids, int count) =>
        ids.OrderByDescending(IdOrder)
            .Take(count)
            .Select(id => (Id: id, Details: monitor.JobDetails(id)))
            .Where(job => job.Details is not null && JobKinds.Of(job.Details.Job?.Type) is not null)
            .Select(job => ToDetails(job.Id, job.Details!, JobKinds.Of(job.Details!.Job?.Type)!));

    // Hangfire job ids are increasing numbers, so the biggest is the newest.
    private static long IdOrder(string id) => long.TryParse(id, out var n) ? n : 0;

    private static JobExecutionDetails ToDetails(string id, JobDetailsDto details, JobKind kind)
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
                : null);
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
