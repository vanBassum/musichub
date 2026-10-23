using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// Carries one claimed, downloaded <see cref="DownloadRequest"/> through the handoff: it finds the file that is already
/// in the downloads folder (never downloading again), hashes it, identifies it with the existing
/// <see cref="IRecordingIdentifier"/> and compares the identity with the wanted track (<see cref="RecordingMatch"/>).
/// Only a file that is confidently the wanted recording is imported through <see cref="AcquisitionHandoff"/> and
/// completes the request. Any other identity, or none, rejects the candidate for this request: the source file stays
/// where it is, the rejection is recorded with its reason, and <see cref="AcquisitionFailover"/> moves the track on to
/// its next candidate. A failure that is not a judgement of the file (a method that could not run, storage, the
/// database) is retried with a growing wait until <see cref="DownloadWorkerOptions.MaxAttempts"/> and then ends the
/// request as ProcessingFailed without rejecting the candidate. Repeating any step is safe: an imported download is not
/// imported again and a rejected one is not judged again. Scoped, like the services it uses.
/// </summary>
public sealed class DownloadHandoffProcessor(
    IDownloadQueueService queue,
    IAcquisitionProvider provider,
    AcquisitionHistory history,
    AcquisitionHandoff handoff,
    IRecordingIdentifier identifier,
    AcquisitionFailover failover,
    MusicHubDbContext db,
    IOptions<DownloadWorkerOptions> options,
    ILogger<DownloadHandoffProcessor> logger)
{
    public async Task ProcessAsync(ClaimedHandoff claim, CancellationToken ct)
    {
        try
        {
            await HandOffAsync(claim, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The request stays Processing, as after a crash: the next start puts it back to Downloaded.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handing off request {RequestId} failed.", claim.RequestId);
            // Whatever the failed step left half-tracked must not be saved with the recovery below.
            db.ChangeTracker.Clear();
            // Only the exception type: the message may carry paths or storage details.
            await RetryOrFailAsync(claim, $"Importing the downloaded file failed ({ex.GetType().Name}).", ct);
        }
    }

    private async Task HandOffAsync(ClaimedHandoff claim, CancellationToken ct)
    {
        var download = (claim.AcquisitionDownloadId is { } id ? await db.AcquisitionDownloads.FindAsync([id], ct) : null)
            ?? await history.BeginAsync(claim.Candidate, claim.TrackId, ct);

        // An earlier try got further than it could report: only the end of the handoff is left.
        if (download.FileId is not null)
        {
            await queue.MarkCompletedAsync(claim.RequestId, ct);
            return;
        }
        if (download.Outcome == AcquisitionOutcome.Rejected)
        {
            await FinishRejectedAsync(claim, download, download.Reason ?? "The file was rejected.", ct);
            return;
        }

        if (claim.TrackId is not { } trackId || await WantedAsync(trackId, ct) is not { } wanted)
        {
            await FailAsync(claim, AcquisitionFailure.ProcessingFailed,
                "The wanted track no longer exists, so the downloaded file was not imported.", ct);
            return;
        }

        var located = await provider.LocateDownloadAsync(claim.Candidate, ct);
        if (located.File is not { } file)
        {
            await FailAsync(claim, located.Failure ?? AcquisitionFailure.FileNotAccessible,
                located.Detail ?? "The downloaded file could not be found.", ct);
            return;
        }

        var hashes = await DownloadHashes.ComputeAsync(file, ct);
        await history.DownloadedAsync(download, hashes, ct);

        IdentificationResult result;
        await using (var audio = file.OpenRead())
            result = await identifier.IdentifyAsync(audio, file.FileName, ct);

        // A method that failed or could not run may have hidden the match: that is not a judgement of the file.
        if (CouldNotJudge(result))
        {
            await RetryOrFailAsync(claim, "The file could not be identified because no identification method could answer.", ct);
            return;
        }

        var (kind, reason) = RecordingMatch.Compare(wanted, result);
        if (kind == MatchKind.Same)
        {
            await handoff.ImportAsync(download, file, hashes, trackId, reason, result.Methods ?? [], ct);
            await queue.MarkCompletedAsync(claim.RequestId, ct);
            return;
        }

        var attempt = history.AttemptOn(download, file.FileName, DecisionFor(kind, result), reason, result.Methods ?? [], null);
        await history.RejectedAsync(download, reason, ct, attempt);
        await FinishRejectedAsync(claim, download, reason, ct);
    }

    /// <summary>The request ends as rejected and the track moves on; the file stays in the downloads folder.</summary>
    private async Task FinishRejectedAsync(ClaimedHandoff claim, AcquisitionDownload download, string reason, CancellationToken ct)
    {
        logger.LogInformation("The file of request {RequestId} was rejected: {Reason}", claim.RequestId, reason);
        await queue.FailAsync(claim.RequestId, AcquisitionFailure.IdentityRejected, reason, download.Id, ct);
        if (claim.TrackId is not { } trackId)
            return;

        try
        {
            await failover.FailOverAsync(trackId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The rejection stands; the next search for the missing track finds the next candidate.
            logger.LogError(ex, "Moving track {TrackId} on after the rejection of request {RequestId} failed.", trackId, claim.RequestId);
        }
    }

    /// <summary>The request ends: the attempt could not be completed and another would not change that.</summary>
    private async Task FailAsync(ClaimedHandoff claim, AcquisitionFailure failure, string detail, CancellationToken ct)
    {
        if (claim.AcquisitionDownloadId is { } id && await db.AcquisitionDownloads.FindAsync([id], ct) is { } download)
            await history.FailedAsync(download, failure, detail, ct);
        await queue.FailAsync(claim.RequestId, failure, detail, claim.AcquisitionDownloadId, ct);
    }

    private Task RetryOrFailAsync(ClaimedHandoff claim, string detail, CancellationToken ct)
    {
        if (options.Value.RetryWait(claim.AttemptNumber) is not { } wait)
            return FailAsync(claim, AcquisitionFailure.ProcessingFailed, $"{detail} No attempts are left; the file was not imported.", ct);

        return queue.RetryHandoffLaterAsync(
            claim.RequestId, AcquisitionFailure.ProcessingFailed, $"{detail} It is tried again later.", wait, ct);
    }

    private async Task<WantedRecording?> WantedAsync(int trackId, CancellationToken ct)
    {
        var track = await db.Tracks.AsNoTracking()
            .Include(t => t.Artists).ThenInclude(a => a.Artist)
            .FirstOrDefaultAsync(t => t.Id == trackId, ct);
        return track is null ? null : WantedRecording.Of(track);
    }

    private static bool CouldNotJudge(IdentificationResult result) =>
        result.Status == IdentificationStatus.Unresolved
        && IdentificationDecisions.Classify(result.Methods ?? [])
            is IdentificationDecision.MethodFailed or IdentificationDecision.NoMethodRan;

    private static IdentificationDecision DecisionFor(MatchKind kind, IdentificationResult result) => kind switch
    {
        MatchKind.Different => IdentificationDecision.Mismatch,
        _ when result.Status == IdentificationStatus.Identified => IdentificationDecision.InsufficientEvidence,
        _ => IdentificationDecisions.Classify(result.Methods ?? []),
    };
}
