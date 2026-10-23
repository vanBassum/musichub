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
public class MissingMusicAcquisitionJob(MissingMusicAcquisition acquisition, TrackSearchRequests searchRequests)
{
    /// <summary>
    /// The same run for one track, started by the wishlist's Search now. It has no concurrency lock: it never waits for
    /// the library run, and the track's request in <see cref="TrackSearchRequests"/> keeps it from being scheduled twice.
    /// </summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task<MissingMusicSummary> RunForTrack(int trackId, PerformContext? context, CancellationToken cancellationToken)
    {
        try
        {
            return await acquisition.RunAsync(
                context?.BackgroundJob.Id, total => JobProgress.ReportTotal(context, total), cancellationToken, trackId);
        }
        finally
        {
            searchRequests.Release(trackId);
        }
    }

    /// <param name="context">Supplied by Hangfire when the job runs; its job id names the execution.</param>
    [DisableConcurrentExecution(timeoutInSeconds: 30)]
    [AutomaticRetry(Attempts = 0)]
    public Task<MissingMusicSummary> Run(PerformContext? context, CancellationToken cancellationToken) =>
        acquisition.RunAsync(
            context?.BackgroundJob.Id, total => JobProgress.ReportTotal(context, total), cancellationToken);
}
