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
/// The received bytes are checked against the client's declared length and SHA-256; a short, long
/// or corrupt transfer never reaches Stored and its object is removed.
/// </summary>
public sealed class FileIngestionService(MusicHubDbContext db, IFileStorage storage)
{
    /// <summary>
    /// Returns only a record with status Stored; every other outcome throws.
    /// Throws <see cref="FileUploadRejectedException"/> when the received bytes differ from <paramref name="expected"/>.
    /// </summary>
    public async Task<FileRecord> IngestAsync(
        Stream content,
        string filename,
        string? contentType,
        ExpectedUpload expected,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var idStr = id.ToString("D"); // e.g. "5f8a2c91-7b43-4c90-a831-1d23e4567890"
        var storageKey = $"library/{idStr[..2]}/{idStr}";

        var record = new FileRecord
        {
            OriginalFilename = filename,
            StorageLocation = "default",
            StorageKey = storageKey,
            Status = FileStatus.Pending,
            Source = "upload",
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.Files.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        using var hashingStream = new Sha256HashingStream(content);
        try
        {
            record.Status = FileStatus.Uploading;
            await db.SaveChangesAsync(cancellationToken);

            Exception? putError = null;
            try
            {
                await storage.PutAsync(storageKey, hashingStream, contentType, cancellationToken);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or BadHttpRequestException))
            {
                putError = ex;
            }

            var mismatch = DescribeMismatch(hashingStream, expected);
            if (mismatch is { } rejected)
            {
                await TryDeleteObjectAsync(storageKey);
                await TryMarkFailedAsync(record, rejected.Reason);
                throw new FileUploadRejectedException(record.Id, rejected.Reason, rejected.Detail);
            }

            // The bytes the API received are intact. A failed PUT may still have stored them:
            // only a conclusive check of the object's length and content counts, not its existence.
            if (putError is not null && !await IsStoredIntactAsync(storageKey, expected, cancellationToken))
            {
                await TryDeleteObjectAsync(storageKey);
                await TryMarkFailedAsync(record, FileFailureReasons.StorageError);
                throw new FileIngestionException(record.Id, putError);
            }

            record.Status = FileStatus.Stored;
            record.ContentHash = hashingStream.GetHashHex();
            record.SizeBytes = hashingStream.BytesRead;
            record.FailureReason = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is FileUploadRejectedException or FileIngestionException)
        {
            throw;
        }
        catch (Exception ex) when (ex is BadHttpRequestException)
        {
            // A client error while reading the body (e.g. Kestrel's 413 for an over-limit chunked
            // upload): the file is not stored, but it is not a storage failure either.
            await TryMarkFailedAsync(record, FileFailureReasons.Incomplete);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await TryMarkFailedAsync(record, FileFailureReasons.StorageError);
            throw new FileIngestionException(record.Id, ex);
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
        var record = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (record is not { Status: FileStatus.Stored })
            return null;

        var content = await storage.OpenReadAsync(record.StorageKey, cancellationToken);
        return (record, content);
    }

    private static (string Reason, string Detail)? DescribeMismatch(Sha256HashingStream received, ExpectedUpload expected)
    {
        if (received.BytesRead < expected.Length)
            return (FileFailureReasons.Incomplete, $"Upload incomplete: received {received.BytesRead} of {expected.Length} bytes.");
        if (received.BytesRead > expected.Length)
            return (FileFailureReasons.Corrupt, $"Upload longer than declared: expected {expected.Length} bytes.");
        if (!string.Equals(received.GetHashHex(), expected.Sha256Hex, StringComparison.OrdinalIgnoreCase))
            return (FileFailureReasons.Corrupt, "Upload corrupt: the SHA-256 of the received bytes differs from the declared digest.");
        return null;
    }

    private async Task<bool> IsStoredIntactAsync(string key, ExpectedUpload expected, CancellationToken cancellationToken)
    {
        try
        {
            await using var stored = await storage.OpenReadAsync(key, cancellationToken);
            using var hashing = new Sha256HashingStream(stored);
            await hashing.CopyToAsync(Stream.Null, cancellationToken);
            return DescribeMismatch(hashing, expected) is null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task TryDeleteObjectAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch
        {
            // Best effort: the record is Failed, so the object is never served.
        }
    }

    private async Task TryMarkFailedAsync(FileRecord record, string reason)
    {
        try
        {
            record.Status = FileStatus.Failed;
            record.FailureReason = reason;
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
