using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Options;
using MusicHub.Api.Catalog;

namespace MusicHub.Api.Jobs;

/// <summary>
/// The Hangfire job that links Stored files without a track to the catalog. Only one run executes at a time, across
/// instances (Hangfire's distributed lock); a run that cannot get it within the timeout fails and is retried by Hangfire.
/// Its return value, the summary, is kept as the job's result; the per-file outcomes are recorded under the job's id and
/// stay as identification history after Hangfire drops the execution (only Skipped ones are pruned).
/// </summary>
public class CatalogReconciliationJob(CatalogReconciliation reconciliation, IOptions<HangfireOptions> options)
{
    /// <param name="context">Supplied by Hangfire when the job runs; its job id names the execution.</param>
    [DisableConcurrentExecution(timeoutInSeconds: 30)]
    public async Task<CatalogReconciliationSummary> Run(PerformContext? context, CancellationToken cancellationToken)
    {
        await reconciliation.PruneOutcomesAsync(TimeSpan.FromHours(options.Value.RetentionHours), cancellationToken);
        return await reconciliation.RunAsync(context?.BackgroundJob.Id, cancellationToken);
    }
}
