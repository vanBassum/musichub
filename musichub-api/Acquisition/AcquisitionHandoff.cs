using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// Gives a downloaded file that was identified as the wanted recording to the library: it is stored and registered by
/// the existing ingestion like any received file, linked to the track it was wanted for, and the decision is written
/// to the download's permanent history. It decides nothing: <see cref="DownloadHandoffProcessor"/> identifies and
/// validates the file first, and only an accepted file reaches this class. Repeating it for the same download creates
/// no second file record.
/// </summary>
public sealed class AcquisitionHandoff(FileIngestionService ingestion, MusicHubDbContext db, AcquisitionHistory history)
{
    /// <param name="hashes">The length and SHA-256 the bytes must have; ingestion refuses the file when they differ.</param>
    /// <param name="reason">Why the file was accepted, for the history and the identification attempt.</param>
    /// <param name="methods">What each identification method did, kept with the attempt.</param>
    public async Task<FileRecord> ImportAsync(
        AcquisitionDownload download, IAcquiredFile file, DownloadHashes hashes, int trackId, string reason,
        IReadOnlyList<IdentificationMethodResult> methods, CancellationToken ct)
    {
        // A file left behind by an earlier try that ended before its record was linked is reused, never stored again.
        var record = await FindUnlinkedAsync(download.Provider, hashes.ContentHash, trackId, ct)
            ?? await IngestAsync(download.Provider, file, hashes, ct);

        var attempt = history.AttemptOn(download, file.FileName, IdentificationDecision.Identified, reason, methods, trackId);
        attempt.FileId = record.Id;

        // One transaction: the link, the applied decision, the attempt and the download's outcome are stored together.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        record.TrackId = trackId;
        db.AppliedIdentifications.Add(new AppliedIdentification
        {
            FileId = record.Id,
            TrackId = trackId,
            Source = IdentificationSource.Automatic,
            Attempt = attempt,
            AppliedAt = attempt.RecordedAt,
        });
        await history.AcceptedAsync(download, record.Id, reason, ct, attempt);
        await transaction.CommitAsync(ct);
        return record;
    }

    /// <summary>The <see cref="FileRecord.Source"/> of a file that a provider's download became.</summary>
    public static string SourceOf(string provider) => $"acquisition:{provider}";

    private async Task<FileRecord> IngestAsync(string provider, IAcquiredFile file, DownloadHashes hashes, CancellationToken ct)
    {
        await using var content = file.OpenRead();
        var expected = new UploadExpectation(content.Length, hashes.ContentHash);
        return await ingestion.IngestAsync(content, file.FileName, null, expected, ct, source: SourceOf(provider));
    }

    /// <summary>A Stored file of this provider with these bytes that no download owns yet and no other track claims.</summary>
    private Task<FileRecord?> FindUnlinkedAsync(string provider, string contentHash, int trackId, CancellationToken ct)
    {
        var source = SourceOf(provider);
        return db.Files
            .Where(f => f.Status == FileStatus.Stored && f.ContentHash == contentHash && f.Source == source
                && (f.TrackId == null || f.TrackId == trackId)
                && !db.AcquisitionDownloads.Any(d => d.FileId == f.Id))
            .OrderBy(f => f.Id)
            .FirstOrDefaultAsync(ct);
    }
}
