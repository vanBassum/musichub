using Hangfire;

namespace MusicHub.Api.Jobs;

/// <summary>
/// The internal way to run the ping job: enqueue it and read its Hangfire state. Not reachable over HTTP; the
/// jobs that follow (catalog reconciliation) are triggered the same way, from application code.
/// </summary>
public sealed class PingJobService(JobEngine engine)
{
    public async Task<string> EnqueueAsync(CancellationToken cancellationToken)
    {
        var client = await engine.ClientAsync(cancellationToken);
        return client.Enqueue<PingJob>(job => job.Run());
    }

    /// <summary>The Hangfire state name of the job (Enqueued, Processing, Succeeded, Failed, Scheduled, …), or null when it is unknown.</summary>
    public async Task<string?> GetStateAsync(string jobId, CancellationToken cancellationToken)
    {
        var storage = await engine.StorageAsync(cancellationToken);
        return storage.GetMonitoringApi().JobDetails(jobId)?.History.FirstOrDefault()?.StateName;
    }
}
