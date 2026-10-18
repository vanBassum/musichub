using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;
using MusicHub.Api.Storage;

namespace MusicHub.Api.Ingestion;

public enum FileDeletionOutcome
{
    /// <summary>The owned object is gone and the registration is removed.</summary>
    Deleted,
    /// <summary>No registration with this id: never existed, or already deleted. Nothing was changed.</summary>
    NotFound,
    /// <summary>An upload for this file is still running in this process; nothing was changed.</summary>
    UploadInProgress,
}

/// <summary>Storage cleanup did not finish. The registration stays Deleting with the failure kind and can be retried.</summary>
public sealed class FileDeletionException(int fileId, string detail, Exception? inner = null)
    : Exception("The file's storage could not be removed.", inner)
{
    public int FileId { get; } = fileId;

    /// <summary>A safe sentence naming the kind of failure, never a message, key or credential.</summary>
    public string Detail { get; } = detail;
}

/// <summary>
/// The one place a file is deleted, whatever its status. The registration is first made Deleting (durable intent, so
/// an interrupted deletion is visible and retryable), then the owned object is removed and its absence confirmed, and
/// only then the registration row is deleted: no tombstone remains. Tracks and artists are never touched. Idempotent:
/// deleting a Deleting file resumes, and deleting a file that is already gone is NotFound.
/// Concurrent requests for one file are serialized in this process, and a file whose upload is running here is refused.
/// </summary>
public sealed class FileDeletionService(
    MusicHubDbContext db,
    IFileStorage storage,
    ActiveUploadTracker activeUploads,
    ILogger<FileDeletionService> logger)
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

    public async Task<FileDeletionOutcome> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var gate = Locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await DeleteLockedAsync(id, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<FileDeletionOutcome> DeleteLockedAsync(int id, CancellationToken cancellationToken)
    {
        var record = await db.Files.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (record is null)
            return FileDeletionOutcome.NotFound;
        if (activeUploads.IsActive(record.StorageKey))
            return FileDeletionOutcome.UploadInProgress;

        if (record.Status != FileStatus.Deleting)
        {
            record.Status = FileStatus.Deleting;
            await db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            // The storage key is unique to this registration, so the object under it is the file's own remnant.
            await storage.DeleteAsync(record.StorageKey, cancellationToken);
            if (await storage.ExistsAsync(record.StorageKey, cancellationToken))
                throw new FileDeletionException(id, "The object is still present in storage after the delete.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failure = ex as FileDeletionException ?? new FileDeletionException(
                id, $"The storage backend failed ({FileIngestionException.Describe(ex)}); the object may still exist.", ex);
            logger.LogError(ex, "Deleting the storage of file {FileId} failed; it stays Deleting.", id);
            record.DeletionError = failure.Detail;
            await db.SaveChangesAsync(CancellationToken.None);
            throw failure;
        }

        // The object is confirmed gone: remove the registration. If another process already did, the goal is met.
        db.Files.Remove(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogInformation("File {FileId} was already removed by another request.", id);
            return FileDeletionOutcome.Deleted;
        }

        logger.LogInformation("File {FileId} deleted: object and registration removed.", id);
        return FileDeletionOutcome.Deleted;
    }
}
