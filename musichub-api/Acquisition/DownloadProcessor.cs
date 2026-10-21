using Microsoft.Extensions.Options;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// Carries one claimed <see cref="DownloadRequest"/> through its transfer: follows the transfer at the provider (one
/// already under way after a restart is followed, never queued again), persists its progress, and records how the
/// attempt ended, both on the request and as an <see cref="AcquisitionDownload"/> that stays diagnosable. A delivered
/// file moves the request to Downloaded; identifying and importing it, and so Completed, belong to the handoff after
/// the download. A failed attempt is retried with a growing wait until <see cref="DownloadWorkerOptions.MaxAttempts"/>,
/// except a failure that another attempt at the same source cannot change. Scoped, like the services it uses.
/// </summary>
public sealed class DownloadProcessor(
    IDownloadQueueService queue,
    IAcquisitionProvider provider,
    AcquisitionHistory history,
    IOptions<DownloadWorkerOptions> options,
    TimeProvider time,
    ILogger<DownloadProcessor> logger)
{
    /// <summary>Why another attempt at the same source is pointless: the source said no, or MusicHub cannot read what arrived.</summary>
    private static readonly AcquisitionFailure[] Final = [AcquisitionFailure.Rejected, AcquisitionFailure.FileNotAccessible];

    public async Task ProcessAsync(ClaimedDownload claim, CancellationToken ct)
    {
        var download = await history.BeginAsync(claim.Candidate, claim.TrackId, ct);
        var outcome = await TransferAsync(claim, download, ct);

        if (outcome.File is not null)
        {
            await queue.MarkDownloadedAsync(claim.RequestId, download.Id, ct);
            return;
        }

        var failure = outcome.Failure ?? AcquisitionFailure.TransferFailed;
        var detail = outcome.Detail ?? "";
        await history.FailedAsync(download, failure, detail, ct);
        if (Final.Contains(failure) || claim.AttemptNumber >= options.Value.MaxAttempts)
        {
            await queue.FailAsync(claim.RequestId, failure, detail, download.Id, ct);
            return;
        }

        var wait = TimeSpan.FromSeconds(options.Value.RetryDelaySeconds * claim.AttemptNumber);
        await queue.RetryLaterAsync(claim.RequestId, failure, detail, time.GetUtcNow() + wait, download.Id, ct);
    }

    private async Task<AcquisitionDownloadOutcome> TransferAsync(
        ClaimedDownload claim, AcquisitionDownload download, CancellationToken ct)
    {
        try
        {
            return await provider.ResumeDownloadAsync(
                claim.Candidate, bytes => queue.ReportProgressAsync(claim.RequestId, bytes, ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The request stays Downloading, as after a crash: the next start requeues it and the transfer, which
            // slskd keeps, is followed again.
            await history.CancelledAsync(download, "MusicHub stopped during the transfer; it continues after the restart.",
                CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Downloading request {RequestId} failed.", claim.RequestId);
            // Only the exception type: the message may carry paths or storage details.
            return AcquisitionDownloadOutcome.Failed(claim.Candidate.Provider, AcquisitionFailure.ProcessingFailed,
                $"Downloading failed ({ex.GetType().Name}); it is tried again unless the attempts are used up.");
        }
    }
}
