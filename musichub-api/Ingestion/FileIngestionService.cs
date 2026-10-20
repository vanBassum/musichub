using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;
using MusicHub.Api.Storage;

namespace MusicHub.Api.Ingestion;

/// <summary>
/// Stores a file in managed storage and registers a factual record.
/// Write order: the database record is created first (with a null hash), the bytes are
/// streamed to storage while SHA-256 is accumulated, and the hash is written back only
/// after storage confirms success, together with the byte count and status Stored.
/// Cancellation or process termination may leave Pending or Uploading; that never means stored.
/// When the client declared a length and SHA-256, the bytes read must match both before the file
/// is Stored. After a storage error that follows a complete read, the object is read back and must
/// match the bytes received; the mere existence of an object proves nothing.
/// </summary>
public sealed class FileIngestionService(
    MusicHubDbContext db,
    IFileStorage storage,
    ActiveUploadTracker activeUploads,
    ILogger<FileIngestionService>? logger = null)
{
    /// <summary>
    /// Returns only a record with status Stored; every other outcome throws. <paramref name="source"/> records
    /// where the bytes came from (an upload, or an acquisition provider).
    /// </summary>
    public async Task<FileRecord> IngestAsync(
        Stream content,
        string filename,
        string? contentType,
        UploadExpectation? expected,
        CancellationToken cancellationToken,
        string source = "upload")
    {
        var id = Guid.NewGuid();
        var idStr = id.ToString("D"); // e.g. "5f8a2c91-7b43-4c90-a831-1d23e4567890"
        var storageKey = $"library/{idStr[..2]}/{idStr}";

        // Registered before the record exists and released only when this call ends, so the stale-upload
        // cleanup can never expire a registration whose upload is still running.
        using var active = activeUploads.Begin(storageKey);

        var record = new FileRecord
        {
            OriginalFilename = filename,
            StorageLocation = "default",
            StorageKey = storageKey,
            Status = FileStatus.Pending,
            Source = source,
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.Files.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        using var hashingStream = new Sha256HashingStream(content);
        try
        {
            record.Status = FileStatus.Uploading;
            await db.SaveChangesAsync(cancellationToken);

            try
            {
                await storage.PutAsync(storageKey, hashingStream, contentType, cancellationToken);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or BadHttpRequestException))
            {
                // The client may throw after the object was committed. Only a read-back of the object that
                // matches every byte received is conclusive; anything less stays a failure.
                if (!await StorageHoldsReceivedBytesAsync(storageKey, hashingStream, cancellationToken))
                    throw;
                logger?.LogWarning(ex,
                    "Storage reported an error for file {FileId} but the stored object matches the received bytes.",
                    record.Id);
            }

            VerifyReceived(record.Id, hashingStream, expected);

            record.Status = FileStatus.Stored;
            record.ContentHash = hashingStream.GetHashHex();
            record.SizeBytes = hashingStream.BytesRead;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (UploadIntegrityException ex)
        {
            logger?.LogWarning("Upload {FileId} rejected: {Failure}.", record.Id, ex.Failure);
            await TryMarkFailedAsync(record, ex.Failure, ex.Detail);
            throw;
        }
        catch (Exception ex) when (ex is BadHttpRequestException)
        {
            // A client error while reading the body (e.g. Kestrel's 413 for an over-limit chunked
            // upload): the file is not stored, but it is not a storage failure either.
            await TryMarkFailedAsync(record, UploadIntegrityFailure.Incomplete, "The request body could not be read completely.");
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogError(ex, "Storing file {FileId} failed.", record.Id);
            var failure = new FileIngestionException(record.Id, ex);
            await TryMarkFailedAsync(record, UploadIntegrityFailure.StorageError, failure.Detail);
            throw failure;
        }

        return record;
    }

    /// <summary>Lists persisted file registrations, newest first (Id breaks ties).</summary>
    public async Task<IReadOnlyList<FileRecord>> ListAsync(CancellationToken cancellationToken) =>
        await db.Files
            .AsNoTracking()
            .OrderByDescending(f => f.StartedAt)
            .ThenByDescending(f => f.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Opens the stored bytes of registration <paramref name="id"/> for reading.
    /// Returns null when the registration does not exist or is not Stored.
    /// The caller owns the returned stream. Throws <see cref="FileNotFoundException"/> when a Stored
    /// record has no object in storage.
    /// </summary>
    public async Task<(FileRecord Record, Stream Content)?> OpenStoredAsync(int id, CancellationToken cancellationToken)
    {
        var record = await FindStoredAsync(id, cancellationToken);
        if (record is null)
            return null;

        var content = await storage.OpenReadAsync(record.StorageKey, cancellationToken);
        return (record, content);
    }

    /// <summary>Returns the registration <paramref name="id"/> when it is Stored; null when it doesn't exist or is not Stored.</summary>
    public async Task<FileRecord?> FindStoredAsync(int id, CancellationToken cancellationToken)
    {
        var record = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        return record is { Status: FileStatus.Stored } ? record : null;
    }

    /// <summary>
    /// Opens bytes <paramref name="start"/> to <paramref name="end"/> (inclusive) of a Stored record's object.
    /// The caller owns the stream. Throws <see cref="FileNotFoundException"/> when the object is gone.
    /// </summary>
    public Task<Stream> OpenStoredRangeAsync(FileRecord record, long start, long end, CancellationToken cancellationToken) =>
        storage.OpenReadRangeAsync(record.StorageKey, start, end, cancellationToken);

    private static void VerifyReceived(int fileId, Sha256HashingStream body, UploadExpectation? expected)
    {
        if (expected is null)
            return;
        if (body.BytesRead < expected.Length)
            throw new UploadIntegrityException(fileId, UploadIntegrityFailure.Incomplete);
        if (body.BytesRead > expected.Length || body.GetHashHex() != expected.Sha256)
            throw new UploadIntegrityException(fileId, UploadIntegrityFailure.Corrupt);
    }

    /// <summary>
    /// True only when the whole body was read and the object under <paramref name="key"/> has the same
    /// length and SHA-256 as the bytes received. Any storage error counts as inconclusive.
    /// </summary>
    private async Task<bool> StorageHoldsReceivedBytesAsync(string key, Sha256HashingStream body, CancellationToken cancellationToken)
    {
        if (!body.ReachedEnd)
            return false;
        try
        {
            await using var stored = await storage.OpenReadAsync(key, cancellationToken);
            using var readBack = new Sha256HashingStream(stored);
            await readBack.CopyToAsync(Stream.Null, cancellationToken);
            return readBack.BytesRead == body.BytesRead && readBack.GetHashHex() == body.GetHashHex();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task TryMarkFailedAsync(FileRecord record, UploadIntegrityFailure reason, string detail)
    {
        try
        {
            record.Status = FileStatus.Failed;
            record.FailureReason = reason;
            record.StatusDetail = detail;
            record.ContentHash = null;
            record.SizeBytes = null;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // Best effort: the record keeps its earlier state, which never implies success.
        }
    }
}
