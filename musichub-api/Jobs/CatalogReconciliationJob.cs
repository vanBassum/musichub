using Hangfire;
using Hangfire.Server;
using MusicHub.Api.Catalog;

namespace MusicHub.Api.Jobs;

/// <summary>
/// The Hangfire job that links Stored files without a track to the catalog (and, when asked, identifies the linked ones again without changing them). Only one run executes at a time, across
/// instances (Hangfire's distributed lock); a run that cannot get it within the timeout fails and is retried by Hangfire.
/// Its return value, the summary, is kept as the job's result for as long as Hangfire keeps the execution; the per-file
/// attempts are permanent and are not pruned with it.
/// </summary>
public class CatalogReconciliationJob(CatalogReconciliation reconciliation)
{
    /// <param name="context">Supplied by Hangfire when the job runs; its job id names the execution.</param>
    /// <param name="reIdentifyAll">Also identify files that already have a track, without changing their link.</param>
    [DisableConcurrentExecution(timeoutInSeconds: 30)]
    public Task<CatalogReconciliationSummary> Run(PerformContext? context, bool reIdentifyAll, CancellationToken cancellationToken) =>
        reconciliation.RunAsync(context?.BackgroundJob.Id, cancellationToken, reIdentifyAll);
}
