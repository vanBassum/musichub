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

/// <summary>What a run did for one track. <paramref name="DownloadId"/> is the <see cref="DownloadHistory"/> of the import, when there was one.</summary>
public sealed record TrackAcquisitionResult(
    int TrackId, string Title, string Artists, TrackAcquisitionOutcome Outcome, string Detail, int? DownloadId = null);

/// <summary>
/// Acquires one missing track: searches, tries the best-ranked candidates one at a time, and imports the first that is
/// identified as the wanted recording. A downloaded file is judged only by identification, never by what its source
/// claimed; one that is not accepted stays outside the library (in the provider's download area) and is recorded as
/// rejected, so it is not downloaded again. Every attempted download is a <see cref="DownloadHistory"/> with the hashes
/// of its bytes, and every identification of it is a permanent <see cref="IdentificationOutcome"/>. Nothing existing in
/// the library is changed: an import adds one file and links it to the track.
/// </summary>
public sealed class TrackAcquisition(
    MusicHubDbContext db,
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
        var known = await RejectedOrImportedAsync(trackId, cancellationToken);
        var fresh = plausible.Where(c => !known.Contains((c.Provider, c.Source, c.Path))).ToList();
        if (fresh.Count == 0)
            return Result(TrackAcquisitionOutcome.NothingFound,
                $"{offered.Count} results, {plausible.Count} plausible MP3s, {plausible.Count - fresh.Count} of them rejected or imported earlier.");

        var tried = new List<DownloadHistory>();
        foreach (var candidate in fresh.Take(options.Value.MaxDownloadsPerTrack))
        {
            var download = await TryAsync(track, wanted, candidate, executionId, cancellationToken);
            if (download.Status == DownloadStatus.Imported)
                return Result(TrackAcquisitionOutcome.Imported, download.Reason, download.Id);
            tried.Add(download);
        }

        return Result(TrackAcquisitionOutcome.NoAcceptableCandidate,
            $"Tried {tried.Count} of {fresh.Count} candidates, none accepted. Last: {tried[^1].Reason}");
    }

    private Task<bool> HasStoredAudioAsync(int trackId, CancellationToken cancellationToken) =>
        db.Files.AnyAsync(f => f.TrackId == trackId && f.Status == FileStatus.Stored, cancellationToken);

    // A candidate that was rejected for this track, or already imported for it, is not downloaded again. A failed or
    // cancelled transfer judged nothing, so it may be tried again.
    private async Task<HashSet<(string Provider, string Source, string Path)>> RejectedOrImportedAsync(
        int trackId, CancellationToken cancellationToken)
    {
        var rows = await db.DownloadHistories.AsNoTracking()
            .Where(d => d.TrackId == trackId && (d.Status == DownloadStatus.Rejected || d.Status == DownloadStatus.Imported))
            .Select(d => new { d.Provider, d.Source, d.SourcePath })
            .ToListAsync(cancellationToken);
        return rows.Select(d => (d.Provider, d.Source, d.SourcePath)).ToHashSet();
    }

    private async Task<DownloadHistory> TryAsync(
        Track track, WantedRecording wanted, AcquisitionCandidate candidate, string executionId, CancellationToken cancellationToken)
    {
        var download = new DownloadHistory
        {
            TrackId = track.Id,
            Provider = candidate.Provider,
            Source = Cut(candidate.Source, DownloadHistory.SourceMaxLength),
            SourcePath = Cut(candidate.Path, DownloadHistory.PathMaxLength),
            SizeBytes = candidate.SizeBytes,
            BitrateKbps = candidate.BitrateKbps,
            Status = DownloadStatus.Downloading,
            JobExecutionId = executionId,
            StartedAt = time.GetUtcNow(),
        };
        db.DownloadHistories.Add(download);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var outcome = await provider.DownloadAsync(candidate, cancellationToken);
            if (outcome.File is null)
                await FinishAsync(download, DownloadStatus.Failed, $"{outcome.Failure}: {outcome.Detail}", cancellationToken);
            else
                await EvaluateAsync(track, wanted, download, outcome, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FinishAsync(download, DownloadStatus.Cancelled, "The run was cancelled; the candidate may be tried again.", CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Acquiring {Path} for track {TrackId} failed.", candidate.Path, track.Id);
            // Only the exception type: the message may carry paths or storage details.
            await FinishAsync(download, DownloadStatus.Failed, $"Processing failed ({ex.GetType().Name}); the candidate may be tried again.", CancellationToken.None);
        }
        return download;
    }

    // The downloaded bytes: hash them (kept whatever follows), then identify them, and import only an identification
    // that is the wanted recording.
    private async Task EvaluateAsync(
        Track track, WantedRecording wanted, DownloadHistory download, AcquisitionDownloadOutcome outcome, CancellationToken cancellationToken)
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
            await FinishAsync(download, DownloadStatus.Failed,
                $"The downloaded file could not be read ({ex.GetType().Name}).", cancellationToken);
            return;
        }

        download.FileSha256 = hashes.FileSha256;
        download.AudioSha256 = hashes.AudioSha256;
        download.AudioHashAlgorithm = hashes.AudioHashAlgorithm;
        await db.SaveChangesAsync(cancellationToken);

        // The same bytes were rejected for this track before: nothing new to find out. Equal bytes say nothing about
        // any other track, so only a rejection for this one counts.
        var earlier = await db.DownloadHistories.AsNoTracking()
            .Where(d => d.Id != download.Id && d.TrackId == track.Id && d.Status == DownloadStatus.Rejected
                        && d.FileSha256 == hashes.FileSha256)
            .OrderBy(d => d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (earlier is not null)
        {
            await FinishAsync(download, DownloadStatus.Rejected,
                $"The same bytes were already rejected (download #{earlier.Id}: {earlier.Reason})", cancellationToken);
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
        Track track, DownloadHistory download, AcquisitionDownloadOutcome outcome, FileHashes hashes,
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
            await FinishAsync(download, DownloadStatus.Failed, failure, cancellationToken);
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
            Finish(download, DownloadStatus.Imported, $"{reason} Imported as file #{stored.Id}.");
            download.File = stored;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            CatalogReconciliation.CatalogLock.Release();
        }
        logger.LogInformation("Track {TrackId} acquired: download {DownloadId} imported as file {FileId}.", track.Id, download.Id, stored.Id);
    }

    private Task RejectAsync(
        DownloadHistory download, string fileName, IdentificationDecision decision,
        IReadOnlyList<IdentificationMethodResult> methods, string reason, CancellationToken cancellationToken)
    {
        AddAttempt(download, fileName, decision, methods, reason, null);
        return FinishAsync(download, DownloadStatus.Rejected, reason, cancellationToken);
    }

    // Adds the attempt to the context; the caller saves it together with what else belongs to it.
    private IdentificationOutcome AddAttempt(
        DownloadHistory download, string fileName, IdentificationDecision decision,
        IReadOnlyList<IdentificationMethodResult> methods, string reason, int? trackId)
    {
        var attempt = new IdentificationOutcome
        {
            JobExecutionId = download.JobExecutionId,
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

    private void Finish(DownloadHistory download, DownloadStatus status, string reason)
    {
        download.Status = status;
        download.Reason = Cut(reason, DownloadHistory.ReasonMaxLength);
        download.FinishedAt = time.GetUtcNow();
    }

    private Task FinishAsync(DownloadHistory download, DownloadStatus status, string reason, CancellationToken cancellationToken)
    {
        Finish(download, status, reason);
        return db.SaveChangesAsync(cancellationToken);
    }

    private static string Cut(string value, int maxLength) => IdentificationOutcome.Truncate(value, maxLength);
}
