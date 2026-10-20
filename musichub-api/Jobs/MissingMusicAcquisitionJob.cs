using Hangfire;
using Hangfire.Server;
using MusicHub.Api.Acquisition;

namespace MusicHub.Api.Jobs;

/// <summary>
/// The Hangfire job behind Search for Missing Music. Only one run executes at a time, across instances (Hangfire's
/// distributed lock). It is never retried by Hangfire on failure (a retry would search and download everything again):
/// the person starts it again, and the history decides what is not downloaded twice. When the server stops, Hangfire
/// requeues it and the run starts over the same way. Its return value, the summary, is kept as the job's result for as
/// long as Hangfire keeps the execution; the downloads and identification attempts are permanent.
/// </summary>
public class MissingMusicAcquisitionJob(MissingMusicAcquisition acquisition)
{
    /// <param name="context">Supplied by Hangfire when the job runs; its job id names the execution.</param>
    [DisableConcurrentExecution(timeoutInSeconds: 30)]
    [AutomaticRetry(Attempts = 0)]
    public Task<MissingMusicSummary> Run(PerformContext? context, CancellationToken cancellationToken) =>
        acquisition.RunAsync(
            context?.BackgroundJob.Id, total => JobProgress.ReportTotal(context, total), cancellationToken);
}
