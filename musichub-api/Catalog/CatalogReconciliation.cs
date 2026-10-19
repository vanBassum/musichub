using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Storage;

namespace MusicHub.Api.Catalog;

/// <summary>What one reconciliation run did. Files that stayed unlinked (unresolved or failed) are unchanged.</summary>
/// <param name="Examined">Stored, unlinked files the run went through.</param>
/// <param name="Linked">Files now linked to a track.</param>
/// <param name="TracksCreated">Of those links, the tracks that were created for them (the others reused an existing track).</param>
/// <param name="Unresolved">Files the identification could not establish an identity for.</param>
/// <param name="Skipped">Files that were no longer eligible when their turn came (deleted, linked or changed meanwhile).</param>
/// <param name="Failed">Files whose processing failed (storage, identification or database); they stay unlinked.</param>
public sealed record CatalogReconciliationSummary(
    int Examined, int Linked, int TracksCreated, int Unresolved, int Skipped, int Failed);

/// <summary>
/// Links MusicHub-managed Stored files that have no catalog track to the catalog, using the existing
/// <see cref="IRecordingIdentifier"/> and its confidence policy. A file whose identity is established is linked to the
/// existing track with the same title and the same artists (and no version), or to a new track; an unresolved file is
/// left exactly as it was. One file's failure never stops the others, and a rerun finds only what is still unlinked, so
/// it creates no duplicate tracks, artists or links. The proposed identity carries no version, so a track with a
/// version is never reused for it: versions stay distinct tracks.
/// </summary>
public sealed class CatalogReconciliation(
    MusicHubDbContext db,
    IFileStorage storage,
    IRecordingIdentifier identifier,
    TimeProvider time,
    ILogger<CatalogReconciliation> logger)
{
    // Finding and creating tracks and artists must not interleave between runs in this process (the job itself is
    // exclusive across processes). The wishlist creates tracks too, so it takes the same lock.
    internal static readonly SemaphoreSlim CatalogLock = new(1, 1);

    public async Task<CatalogReconciliationSummary> RunAsync(CancellationToken cancellationToken)
    {
        var ids = await db.Files.AsNoTracking()
            .Where(f => f.Status == FileStatus.Stored && f.TrackId == null)
            .OrderBy(f => f.Id)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Catalog reconciliation started: {Count} stored files without a track.", ids.Count);

        int linked = 0, created = 0, unresolved = 0, skipped = 0, failed = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                switch (await ReconcileAsync(id, cancellationToken))
                {
                    case Outcome.LinkedExisting:
                        linked++;
                        break;
                    case Outcome.LinkedCreated:
                        linked++;
                        created++;
                        break;
                    case Outcome.Unresolved:
                        unresolved++;
                        break;
                    default:
                        skipped++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed++;
                logger.LogError(ex, "Reconciling file {FileId} failed; it stays unlinked.", id);
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
        }

        var summary = new CatalogReconciliationSummary(ids.Count, linked, created, unresolved, skipped, failed);
        logger.LogInformation(
            "Catalog reconciliation finished: {Examined} examined, {Linked} linked ({Created} new tracks), {Unresolved} unresolved, {Skipped} skipped, {Failed} failed.",
            summary.Examined, summary.Linked, summary.TracksCreated, summary.Unresolved, summary.Skipped, summary.Failed);
        return summary;
    }

    private enum Outcome { LinkedExisting, LinkedCreated, Unresolved, Skipped }

    private async Task<Outcome> ReconcileAsync(int fileId, CancellationToken cancellationToken)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is not { Status: FileStatus.Stored, TrackId: null })
            return SkipEligibility(fileId);

        logger.LogInformation("Identifying file {FileId} ({Filename}).", fileId, file.OriginalFilename);
        var result = await IdentifyAsync(file, cancellationToken);
        var evidence = string.Join(", ", result.Evidence.Select(e => e.Source).Distinct());

        if (result.Status != IdentificationStatus.Identified || Normalize(result.Identity) is not { } identity)
        {
            logger.LogInformation(
                "File {FileId} stays unlinked: {Reason} (evidence from: {Evidence}).",
                fileId, result.UnresolvedReason ?? "the proposed identity has no title or no artist", evidence);
            return Outcome.Unresolved;
        }

        await CatalogLock.WaitAsync(cancellationToken);
        try
        {
            // Re-read under the lock: the file may have been linked or deleted while it was being identified.
            var current = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
            if (current is not { Status: FileStatus.Stored, TrackId: null })
                return SkipEligibility(fileId);

            var track = await FindTrackAsync(identity, cancellationToken);
            var createdTrack = track is null;
            track ??= await CreateTrackAsync(identity, cancellationToken);

            // One SaveChanges: the track, its artists and the link are stored together or not at all.
            current.Track = track;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "File {FileId} linked to {Kind} track {TrackId} \"{Title}\" (evidence from: {Evidence}).",
                fileId, createdTrack ? "new" : "existing", track.Id, track.Title, evidence);
            return createdTrack ? Outcome.LinkedCreated : Outcome.LinkedExisting;
        }
        finally
        {
            CatalogLock.Release();
        }
    }

    private Outcome SkipEligibility(int fileId)
    {
        logger.LogInformation("File {FileId} skipped: it is no longer a stored file without a track.", fileId);
        return Outcome.Skipped;
    }

    private async Task<IdentificationResult> IdentifyAsync(FileRecord file, CancellationToken cancellationToken)
    {
        // The identification tools may seek, an object store stream does not: use a seekable temporary copy.
        await using var temp = new FileStream(
            Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        await using (var source = await storage.OpenReadAsync(file.StorageKey, cancellationToken))
            await source.CopyToAsync(temp, cancellationToken);
        temp.Position = 0;
        return await identifier.IdentifyAsync(temp, file.OriginalFilename, cancellationToken);
    }

    private static ProposedRecordingIdentity? Normalize(ProposedRecordingIdentity? identity)
    {
        if (identity is null || string.IsNullOrWhiteSpace(identity.Title))
            return null;
        var artists = identity.Artists
            .Select(a => a?.Trim() ?? "")
            .Where(a => a.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return artists.Count == 0 ? null : identity with { Title = identity.Title.Trim(), Artists = artists };
    }

    private async Task<Track?> FindTrackAsync(ProposedRecordingIdentity identity, CancellationToken cancellationToken)
    {
        var title = identity.Title.ToLower();
        var candidates = await db.Tracks
            .Include(t => t.Artists).ThenInclude(a => a.Artist)
            .Where(t => t.Version == null && t.Title.ToLower() == title)
            .OrderBy(t => t.Id)
            .ToListAsync(cancellationToken);

        var wanted = identity.Artists.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return candidates.FirstOrDefault(t =>
            t.Artists.Select(a => a.Artist.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(wanted));
    }

    private async Task<Track> CreateTrackAsync(ProposedRecordingIdentity identity, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var track = new Track { Title = identity.Title, CreatedAt = now };
        foreach (var name in identity.Artists)
        {
            var lower = name.ToLower();
            var artist = await db.Artists.OrderBy(a => a.Id)
                             .FirstOrDefaultAsync(a => a.Name.ToLower() == lower, cancellationToken)
                         ?? new Artist { Name = name, CreatedAt = now };
            track.Artists.Add(new TrackArtist { Artist = artist });
        }
        db.Tracks.Add(track);
        return track;
    }
}
