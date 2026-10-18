using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MusicHub.Api.Data;
using MusicHub.Api.Storage;

namespace MusicHub.Api.Ingestion;

/// <summary>
/// Resolves file registrations that were left Pending or Uploading by a disconnect, crash or lost response.
/// A registration is only touched when it is older than <see cref="FileIngestionOptions.StaleUploadThreshold"/>
/// and no upload is running for it in this process. Object storage is inspected first:
/// no object means the upload never completed, so the record becomes Expired; an object that is present
/// cannot be verified (the expected size and hash lived only in the finished request), so it is neither
/// marked Stored nor deleted and the record is left for the operator. Idempotent: every pass re-reads the database.
/// </summary>
public sealed class StaleUploadCleanup(
    MusicHubDbContext db,
    IFileStorage storage,
    ActiveUploadTracker activeUploads,
    IOptions<FileIngestionOptions> options,
    TimeProvider time,
    ILogger<StaleUploadCleanup> logger)
{
    public const string ExpiredDetail =
        "The upload did not finish and no object was found in storage. Nothing was stored: upload the file again.";

    /// <summary>Runs one pass and returns how many registrations became Expired.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - options.Value.StaleUploadThreshold;
        var candidates = await db.Files
            .Where(f => (f.Status == FileStatus.Pending || f.Status == FileStatus.Uploading) && f.StartedAt < cutoff)
            .ToListAsync(cancellationToken);

        var expired = 0;
        foreach (var record in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await TryExpireAsync(record, cancellationToken))
                    expired++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Storage or database trouble is not conclusive: leave the record as it is and retry next pass.
                logger.LogWarning(ex, "Stale upload {FileId} could not be resolved; it is left unchanged.", record.Id);
                db.Entry(record).State = EntityState.Detached;
            }
        }
        return expired;
    }

    private async Task<bool> TryExpireAsync(FileRecord record, CancellationToken cancellationToken)
    {
        if (activeUploads.IsActive(record.StorageKey))
            return false;

        if (await storage.ExistsAsync(record.StorageKey, cancellationToken))
        {
            logger.LogWarning(
                "Stale upload {FileId} has an object in storage under {StorageKey} that cannot be verified; it is kept and the record is left unchanged.",
                record.Id, record.StorageKey);
            return false;
        }

        // An upload that started meanwhile registers before it writes, so check again before the transition.
        if (activeUploads.IsActive(record.StorageKey))
            return false;

        // The candidate was loaded before the storage round trip: a deletion (or an upload that finished) may have
        // changed it since. Re-read it so a Deleting/Deleted tombstone is never overwritten with Expired.
        await db.Entry(record).ReloadAsync(cancellationToken);
        if (record.Status is not (FileStatus.Pending or FileStatus.Uploading))
            return false;

        record.Status = FileStatus.Expired;
        record.StatusDetail = ExpiredDetail;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Stale upload {FileId} expired: no object in storage.", record.Id);
        return true;
    }
}
