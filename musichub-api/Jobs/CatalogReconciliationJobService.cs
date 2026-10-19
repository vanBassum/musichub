using Hangfire;

namespace MusicHub.Api.Jobs;

/// <summary>The internal way to start catalog reconciliation: enqueue it. Not reachable over HTTP and not scheduled.</summary>
public sealed class CatalogReconciliationJobService(JobEngine engine)
{
    public async Task<string> EnqueueAsync(CancellationToken cancellationToken)
    {
        var client = await engine.ClientAsync(cancellationToken);
        return client.Enqueue<CatalogReconciliationJob>(job => job.Run(CancellationToken.None));
    }
}
