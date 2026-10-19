using Hangfire;
using MusicHub.Api.Catalog;

namespace MusicHub.Api.Jobs;

/// <summary>
/// The Hangfire job that links Stored files without a track to the catalog. Only one run executes at a time, across
/// instances (Hangfire's distributed lock); a run that cannot get it within the timeout fails and is retried by Hangfire.
/// Its return value, the summary, is kept as the job's result.
/// </summary>
public class CatalogReconciliationJob(CatalogReconciliation reconciliation)
{
    [DisableConcurrentExecution(timeoutInSeconds: 30)]
    public Task<CatalogReconciliationSummary> Run(CancellationToken cancellationToken) =>
        reconciliation.RunAsync(cancellationToken);
}
