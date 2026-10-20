using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Acquisition;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrackAcquisitionOutcome
{
    /// <summary>A candidate was identified as the wanted recording and imported; the track now has a file.</summary>
    Imported,

    /// <summary>Nothing worth downloading was offered (no MP3 that claims this recording, or only candidates rejected before).</summary>
    NothingFound,

    /// <summary>Candidates were downloaded or tried, and none was accepted.</summary>
    NoAcceptableCandidate,

    /// <summary>The provider could not search, so nothing is known about this track.</summary>
    ProviderUnavailable,

    /// <summary>The track had a file, or was gone, when its turn came.</summary>
    Skipped,

    /// <summary>Processing the track failed unexpectedly.</summary>
    Failed,
}

/// <summary>What a run did for one track. <paramref name="DownloadId"/> is the <see cref="AcquisitionDownload"/> of the import, when there was one.</summary>
public sealed record TrackAcquisitionResult(
    int TrackId, string Title, string Artists, TrackAcquisitionOutcome Outcome, string Detail, int? DownloadId = null);

/// <summary>
/// Acquires one missing track: searches, tries the best-ranked candidates one at a time, and imports the first that is
/// identified as the wanted recording. A downloaded file is judged only by identification, never by what its source
/// claimed; one that is not accepted stays outside the library (in the provider's download area) and is recorded as
/// rejected, so it is not downloaded again. Every attempted download is recorded through <see cref="AcquisitionHistory"/>
/// with the hashes of its bytes, and every identification of it is a permanent <see cref="IdentificationOutcome"/>.
/// Nothing existing in the library is changed: an import adds one file and links it to the track.
/// </summary>
public sealed class TrackAcquisition(
    MusicHubDbContext db,
    AcquisitionHistory history,
    IAcquisitionProvider provider,
    IRecordingIdentifier identifier,
    AcquisitionHandoff handoff,
    IOptions<MissingMusicOptions> options,
    TimeProvider time,
    ILogger<TrackAcquisition> logger)
{
    public async Task<TrackAcquisitionResult> RunAsync(int trackId, string executionId, CancellationToken cancellationToken)
    {
        var track = await db.Tracks.AsNoTracking()
            .Include(t => t.Artists).ThenInclude(a => a.Artist)
            .FirstOrDefaultAsync(t => t.Id == trackId, cancellationToken);
        if (track is null || await HasStoredAudioAsync(trackId, cancellationToken))
            return new TrackAcquisitionResult(trackId, track?.Title ?? "", "", TrackAcquisitionOutcome.Skipped,
                "The track was gone or had a stored file when its turn came.");

        var wanted = new WantedRecording(
            track.Title, track.Version, track.Artists.OrderBy(a => a.Id).Select(a => a.Artist.Name).ToList());
        var artists = string.Join(", ", wanted.Artists);
        TrackAcquisitionResult Result(TrackAcquisitionOutcome outcome, string detail, int? downloadId = null) =>
            new(trackId, track.Title, artists, outcome, detail, downloadId);

        IReadOnlyList<AcquisitionCandidate> offered;
        try
        {
            offered = await provider.SearchAsync(new AcquisitionQuery(string.Join(' ', wanted.Artists), wanted.Title), cancellationToken);
        }
        catch (AcquisitionUnavailableException e)
        {
            return Result(TrackAcquisitionOutcome.ProviderUnavailable, e.Message);
        }

        var plausible = CandidateRanking.Rank(wanted, offered);
        // A candidate rejected before is not downloaded again. A failed or cancelled transfer judged nothing, so it may be
        // tried again.
        var fresh = new List<AcquisitionCandidate>();
        foreach (var candidate in plausible)
            if (!await history.WasRejectedAsync(candidate, cancellationToken))
                fresh.Add(candidate);
        if (fresh.Count == 0)
            return Result(TrackAcquisitionOutcome.NothingFound,
                $"{offered.Count} results, {plausible.Count} plausible MP3s, {plausible.Count - fresh.Count} of them rejected earlier.");

        var tried = new List<AcquisitionDownload>();
        foreach (var candidate in fresh.Take(options.Value.MaxDownloadsPerTrack))
        {
            var download = await TryAsync(track, wanted, candidate, executionId, cancellationToken);
            if (download.Status == DownloadStatus.Imported)
                return Result(TrackAcquisitionOutcome.Imported, download.Reason ?? "", download.Id);
            tried.Add(download);
        }

        return Result(TrackAcquisitionOutcome.NoAcceptableCandidate,
            $"Tried {tried.Count} of {fresh.Count} candidates, none accepted. Last: {tried[^1].Reason}");
    }

    private Task<bool> HasStoredAudioAsync(int trackId, CancellationToken cancellationToken) =>
        db.Files.AnyAsync(f => f.TrackId == trackId && f.Status == FileStatus.Stored, cancellationToken);

    private async Task<AcquisitionDownload> TryAsync(
        Track track, WantedRecording wanted, AcquisitionCandidate candidate, string executionId, CancellationToken cancellationToken)
    {
        var download = await history.BeginAsync(candidate, track.Id, cancellationToken, executionId);

        try
        {
            var outcome = await provider.DownloadAsync(candidate, cancellationToken);
            if (outcome.File is null)
                await history.FailedAsync(download, outcome.Failure ?? AcquisitionFailure.TransferFailed, outcome.Detail ?? "", cancellationToken);
            else
                await EvaluateAsync(track, wanted, download, outcome, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await history.CancelledAsync(download, "The run was cancelled; the candidate may be tried again.", CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Acquiring {Path} for track {TrackId} failed.", candidate.Path, track.Id);
            // Only the exception type: the message may carry paths or storage details.
            await history.FailedAsync(download, AcquisitionFailure.ProcessingFailed,
                $"Processing failed ({ex.GetType().Name}); the candidate may be tried again.", CancellationToken.None);
        }
        return download;
    }

    // The downloaded bytes: hash them (kept whatever follows), then identify them, and import only an identification
    // that is the wanted recording.
    private async Task EvaluateAsync(
        Track track, WantedRecording wanted, AcquisitionDownload download, AcquisitionDownloadOutcome outcome, CancellationToken cancellationToken)
    {
        var file = outcome.File!;
        FileHashes hashes;
        try
        {
            await using var content = file.OpenRead();
            hashes = await AudioHashing.ComputeAsync(content, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            await history.FailedAsync(download, AcquisitionFailure.FileNotAccessible,
                $"The downloaded file could not be read ({ex.GetType().Name}).", cancellationToken);
            return;
        }

        await history.DownloadedAsync(download, hashes, cancellationToken);

        // The same bytes were rejected for this track before: nothing new to find out.
        if (await history.WasContentRejectedAsync(hashes.FileSha256, cancellationToken, track.Id))
        {
            await history.RejectedAsync(download, "The same bytes were already rejected.", cancellationToken);
            return;
        }

        IdentificationResult result;
        try
        {
            await using var audio = file.OpenRead();
            result = await identifier.IdentifyAsync(audio, file.FileName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Identifying download {DownloadId} failed.", download.Id);
            await RejectAsync(download, file.FileName, IdentificationDecision.Error, [],
                $"Identification failed ({ex.GetType().Name}); the file is kept outside the library.", cancellationToken);
            return;
        }

        var methods = result.Methods ?? [];
        var (match, reason) = RecordingMatch.Compare(wanted, result);
        if (match == MatchKind.Same)
        {
            await ImportAsync(track, download, outcome, hashes, methods, reason, cancellationToken);
            return;
        }

        var decision = match == MatchKind.Different ? IdentificationDecision.Mismatch
            : result.Status == IdentificationStatus.Identified ? IdentificationDecision.InsufficientEvidence
            : IdentificationDecisions.Classify(methods);
        await RejectAsync(download, file.FileName, decision, methods, reason, cancellationToken);
    }

    private async Task ImportAsync(
        Track track, AcquisitionDownload download, AcquisitionDownloadOutcome outcome, FileHashes hashes,
        IReadOnlyList<IdentificationMethodResult> methods, string reason, CancellationToken cancellationToken)
    {
        FileRecord stored;
        try
        {
            stored = await handoff.IngestAsync(outcome, cancellationToken, new UploadExpectation(hashes.SizeBytes, hashes.FileSha256));
        }
        catch (Exception ex) when (ex is UploadIntegrityException or FileIngestionException)
        {
            // The identification said yes, but the bytes did not reach the library intact: nothing was imported.
            var failure = $"Import failed ({ex.GetType().Name}); nothing was added to the library.";
            AddAttempt(download, outcome.File!.FileName, IdentificationDecision.Error, methods, failure, null);
            await history.FailedAsync(download, AcquisitionFailure.ProcessingFailed, failure, cancellationToken);
            return;
        }

        // One transaction: the link, the attempt, the applied decision and the download's outcome.
        await CatalogReconciliation.CatalogLock.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            stored.TrackId = track.Id;
            var attempt = AddAttempt(download, outcome.File!.FileName, IdentificationDecision.Identified, methods,
                $"{reason} Imported and linked to track \"{track.Title}\".", track.Id);
            db.AppliedIdentifications.Add(new AppliedIdentification
            {
                File = stored,
                TrackId = track.Id,
                Source = IdentificationSource.Automatic,
                Attempt = attempt,
                AppliedAt = attempt.RecordedAt,
            });
            await history.AcceptedAsync(download, stored.Id, $"{reason} Imported as file #{stored.Id}.", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            CatalogReconciliation.CatalogLock.Release();
        }
        logger.LogInformation("Track {TrackId} acquired: download {DownloadId} imported as file {FileId}.", track.Id, download.Id, stored.Id);
    }

    private Task RejectAsync(
        AcquisitionDownload download, string fileName, IdentificationDecision decision,
        IReadOnlyList<IdentificationMethodResult> methods, string reason, CancellationToken cancellationToken)
    {
        AddAttempt(download, fileName, decision, methods, reason, null);
        return history.RejectedAsync(download, reason, cancellationToken);
    }

    // Adds the attempt to the context; the caller saves it together with what else belongs to it.
    private IdentificationOutcome AddAttempt(
        AcquisitionDownload download, string fileName, IdentificationDecision decision,
        IReadOnlyList<IdentificationMethodResult> methods, string reason, int? trackId)
    {
        var attempt = new IdentificationOutcome
        {
            JobExecutionId = download.JobExecutionId ?? "",
            Download = download,
            FileName = IdentificationOutcome.Truncate(fileName, IdentificationOutcome.FileNameMaxLength),
            Decision = decision,
            Reason = IdentificationOutcome.Truncate(reason, IdentificationOutcome.ReasonMaxLength),
            TrackId = trackId,
            RecordedAt = time.GetUtcNow(),
            PipelineVersion = BuildVersion.Current,
        };
        attempt.SetMethods(methods);
        db.IdentificationOutcomes.Add(attempt);
        return attempt;
    }

}
