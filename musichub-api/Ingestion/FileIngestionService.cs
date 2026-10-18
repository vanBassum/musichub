using MusicHub.Api.Data;
using MusicHub.Api.Storage;

namespace MusicHub.Api.Ingestion;

/// <summary>
/// Stores a file in managed storage and registers a factual record.
/// Write order: the database record is created first (with a null hash), the bytes are
/// streamed to storage while SHA-256 is accumulated, and the hash is written back only
/// after storage confirms success. A null hash means the write was not confirmed.
/// </summary>
public sealed class FileIngestionService(MusicHubDbContext db, IFileStorage storage)
{
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
            Source = "upload",
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.Files.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        using var hashingStream = new Sha256HashingStream(content);
        await storage.PutAsync(storageKey, hashingStream, contentType, cancellationToken);

        record.ContentHash = hashingStream.GetHashHex();
        await db.SaveChangesAsync(cancellationToken);

        return record;
    }
}
