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
/// </summary>
public sealed class FileIngestionService(MusicHubDbContext db, IFileStorage storage)
{
    /// <summary>
    /// Returns only a record with status Stored; every other outcome throws.
    /// </summary>
    public async Task<FileRecord> IngestAsync(
        Stream content,
        string filename,
        string? contentType,
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

            await storage.PutAsync(storageKey, hashingStream, contentType, cancellationToken);

            record.Status = FileStatus.Stored;
            record.ContentHash = hashingStream.GetHashHex();
            record.SizeBytes = hashingStream.BytesRead;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is BadHttpRequestException)
        {
            // A client error while reading the body (e.g. Kestrel's 413 for an over-limit chunked
            // upload): the file is not stored, but it is not a storage failure either.
            await TryMarkFailedAsync(record);
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await TryMarkFailedAsync(record);
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

    private async Task TryMarkFailedAsync(FileRecord record)
    {
        try
        {
            record.Status = FileStatus.Failed;
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
