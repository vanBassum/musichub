using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;
using MusicHub.Api.Catalog;

namespace MusicHub.Api.Jobs;

/// <summary>One run of the catalog reconciliation job, read from Hangfire. <paramref name="State"/> is Hangfire's own state name.</summary>
/// <param name="Summary">What the run did; set once it Succeeded.</param>
/// <param name="Error">The message of the last failure (a failed run, or the attempt that is being retried).</param>
public sealed record JobExecution(
    string Id,
    string State,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    CatalogReconciliationSummary? Summary,
    string? Error);

/// <summary>One step of an execution's Hangfire state history, oldest first.</summary>
public sealed record JobStateEntry(string State, DateTime At, string? Reason);

public sealed record JobExecutionDetails(JobExecution Execution, IReadOnlyList<JobStateEntry> History, string? ErrorType);

/// <summary>
/// Starts catalog reconciliation and reads its executions from Hangfire (state history, the summary the job returned,
/// the failure message). It is the only way the API reaches the job; there is no Hangfire dashboard. A run is refused
/// while another one is enqueued, scheduled for a retry, or processing.
/// </summary>
public sealed class CatalogReconciliationJobService(JobEngine engine)
{
    private const string Queue = "default";
    private const int PerState = 50;
    private static readonly string[] FinalStates = ["Succeeded", "Failed", "Deleted"];
    private static readonly SemaphoreSlim StartLock = new(1, 1);

    public bool IsReady => engine.IsReady;

    public async Task<string> EnqueueAsync(CancellationToken cancellationToken)
    {
        var client = await engine.ClientAsync(cancellationToken);
        return client.Enqueue<CatalogReconciliationJob>(job => job.Run(CancellationToken.None));
    }

    /// <summary>Enqueues a run unless one is active. Returns the new execution id, or null when a run is already active.</summary>
    public async Task<string?> TryStartAsync(CancellationToken cancellationToken)
    {
        await StartLock.WaitAsync(cancellationToken);
        try
        {
            if (await ActiveExecutionIdAsync(cancellationToken) is not null)
                return null;
            return await EnqueueAsync(cancellationToken);
        }
        finally
        {
            StartLock.Release();
        }
    }

    /// <summary>The id of the run that is enqueued, waiting for a retry or processing, or null when none is.</summary>
    public async Task<string?> ActiveExecutionIdAsync(CancellationToken cancellationToken)
    {
        var monitor = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi();
        return Ids(monitor.EnqueuedJobs(Queue, 0, PerState), j => j.Job)
            .Concat(Ids(monitor.ProcessingJobs(0, PerState), j => j.Job))
            .Concat(Ids(monitor.ScheduledJobs(0, PerState), j => j.Job))
            .OrderByDescending(IdOrder)
            .FirstOrDefault();
    }

    /// <summary>The newest executions, newest first.</summary>
    public async Task<IReadOnlyList<JobExecution>> ListAsync(int count, CancellationToken cancellationToken)
    {
        var monitor = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi();
        return Ids(monitor.EnqueuedJobs(Queue, 0, PerState), j => j.Job)
            .Concat(Ids(monitor.ProcessingJobs(0, PerState), j => j.Job))
            .Concat(Ids(monitor.ScheduledJobs(0, PerState), j => j.Job))
            .Concat(Ids(monitor.SucceededJobs(0, PerState), j => j.Job))
            .Concat(Ids(monitor.FailedJobs(0, PerState), j => j.Job))
            .Distinct()
            .OrderByDescending(IdOrder)
            .Take(count)
            .Select(id => (Id: id, Details: monitor.JobDetails(id)))
            .Where(job => job.Details is not null)
            .Select(job => ToDetails(job.Id, job.Details!).Execution)
            .ToList();
    }

    public async Task<JobExecutionDetails?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var details = (await engine.StorageAsync(cancellationToken)).GetMonitoringApi().JobDetails(id);
        return details?.Job?.Type == typeof(CatalogReconciliationJob) ? ToDetails(id, details) : null;
    }

    private static IEnumerable<string> Ids<T>(IEnumerable<KeyValuePair<string, T>> jobs, Func<T, Job?> job) =>
        jobs.Where(pair => job(pair.Value)?.Type == typeof(CatalogReconciliationJob)).Select(pair => pair.Key);

    // Hangfire job ids are increasing numbers, so the biggest is the newest.
    private static long IdOrder(string id) => long.TryParse(id, out var n) ? n : 0;

    private static JobExecutionDetails ToDetails(string id, JobDetailsDto details)
    {
        var history = details.History
            .OrderBy(h => h.CreatedAt)
            .Select(h => new JobStateEntry(h.StateName, h.CreatedAt, h.Reason))
            .ToList();
        var last = details.History.OrderBy(h => h.CreatedAt).Last();
        var processing = details.History.Where(h => h.StateName == "Processing").MaxBy(h => h.CreatedAt);
        var succeeded = details.History.FirstOrDefault(h => h.StateName == "Succeeded");
        var failed = details.History.Where(h => h.Data.ContainsKey("ExceptionMessage")).MaxBy(h => h.CreatedAt);
        var finished = FinalStates.Contains(last.StateName);

        var execution = new JobExecution(
            Id: id,
            State: last.StateName,
            CreatedAt: details.CreatedAt ?? history[0].At,
            StartedAt: processing?.CreatedAt,
            FinishedAt: finished ? last.CreatedAt : null,
            Summary: succeeded is null ? null : ReadSummary(succeeded),
            Error: failed?.Data["ExceptionMessage"]);
        return new JobExecutionDetails(execution, history, failed is not null && failed.Data.TryGetValue("ExceptionType", out var type) ? type : null);
    }

    private static CatalogReconciliationSummary? ReadSummary(StateHistoryDto succeeded)
    {
        if (!succeeded.Data.TryGetValue("Result", out var json) || string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<CatalogReconciliationSummary>(json, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
