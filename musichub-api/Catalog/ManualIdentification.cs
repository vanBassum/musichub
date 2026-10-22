using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Catalog;

public enum ManualIdentificationResult { Applied, FileNotFound, TrackNotFound, FileNotStored }

/// <summary>
/// A person's correction of which track a stored file is. It replaces the file's current link and is recorded as a
/// <see cref="IdentificationSource.Manual"/> decision in the same save. Automatic reconciliation never replaces a link
/// whose latest decision is a manual one, not even in a full re-identification.
/// </summary>
public sealed class ManualIdentification(MusicHubDbContext db, TimeProvider time)
{
    public async Task<ManualIdentificationResult> ApplyAsync(int fileId, int trackId, CancellationToken cancellationToken)
    {
        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null)
            return ManualIdentificationResult.FileNotFound;
        if (file.Status != FileStatus.Stored)
            return ManualIdentificationResult.FileNotStored;
        if (!await db.Tracks.AnyAsync(t => t.Id == trackId, cancellationToken))
            return ManualIdentificationResult.TrackNotFound;

        file.TrackId = trackId;
        db.AppliedIdentifications.Add(new AppliedIdentification
        {
            FileId = fileId,
            TrackId = trackId,
            Source = IdentificationSource.Manual,
            AppliedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return ManualIdentificationResult.Applied;
    }
}
