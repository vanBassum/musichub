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

    private readonly ArtistExternalLinks links = new(db, time);

    /// <summary>
    /// Deletes the Skipped outcomes recorded before <paramref name="retention"/> ago (Hangfire keeps its executions that
    /// long too). A skipped file was not examined, so its outcome has nothing to review; every other outcome is permanent
    /// identification history and is never pruned because an execution expired.
    /// </summary>
    public async Task<int> PruneOutcomesAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - retention;
        // Compared in memory: not every provider translates DateTimeOffset comparisons.
        var expired = (await db.IdentificationOutcomes
                .Where(o => o.Decision == IdentificationDecision.Skipped)
                .Select(o => new { o.Id, o.RecordedAt }).ToListAsync(cancellationToken))
            .Where(o => o.RecordedAt < cutoff).Select(o => o.Id).ToList();
        var deleted = 0;
        foreach (var batch in expired.Chunk(1000))
            deleted += await db.IdentificationOutcomes.Where(o => batch.Contains(o.Id)).ExecuteDeleteAsync(cancellationToken);
        return deleted;
    }

    /// <param name="executionId">The Hangfire execution the per-file outcomes are recorded under; null records none.</param>
    public async Task<CatalogReconciliationSummary> RunAsync(string? executionId, CancellationToken cancellationToken)
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
            var attempt = new Attempt();
            try
            {
                switch (await ReconcileAsync(id, attempt, cancellationToken))
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
                // Only the exception type: the message may carry storage or database details.
                attempt.Decision = IdentificationDecision.Error;
                attempt.Reason = $"Processing failed ({ex.GetType().Name}); the file stays unlinked.";
            }
            finally
            {
                db.ChangeTracker.Clear();
            }

            if (executionId is not null)
                await RecordAsync(executionId, id, attempt, cancellationToken);
        }

        var summary = new CatalogReconciliationSummary(ids.Count, linked, created, unresolved, skipped, failed);
        logger.LogInformation(
            "Catalog reconciliation finished: {Examined} examined, {Linked} linked ({Created} new tracks), {Unresolved} unresolved, {Skipped} skipped, {Failed} failed.",
            summary.Examined, summary.Linked, summary.TracksCreated, summary.Unresolved, summary.Skipped, summary.Failed);
        return summary;
    }

    private enum Outcome { LinkedExisting, LinkedCreated, Unresolved, Skipped }

    /// <summary>What one file's attempt turned out to be; filled in as the attempt proceeds, recorded once at the end.</summary>
    private sealed class Attempt
    {
        public string FileName { get; set; } = "";
        public IdentificationDecision Decision { get; set; } = IdentificationDecision.Error;
        public string Reason { get; set; } = "The attempt did not finish.";
        public int? TrackId { get; set; }
        public IReadOnlyList<IdentificationMethodResult> Methods { get; set; } = [];
    }

    // The diagnostic must never fail the file: it only describes what happened.
    private async Task RecordAsync(string executionId, int fileId, Attempt attempt, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = new IdentificationOutcome
            {
                JobExecutionId = executionId,
                FileId = fileId,
                FileName = IdentificationOutcome.Truncate(attempt.FileName, IdentificationOutcome.FileNameMaxLength),
                Decision = attempt.Decision,
                Reason = IdentificationOutcome.Truncate(attempt.Reason, IdentificationOutcome.ReasonMaxLength),
                TrackId = attempt.TrackId,
                RecordedAt = time.GetUtcNow(),
            };
            outcome.SetMethods(attempt.Methods);
            db.IdentificationOutcomes.Add(outcome);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "The identification outcome of file {FileId} could not be recorded.", fileId);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    // Unresolved: said by what the tools did, never by guessing. A failed tool may have hidden a match.
    private static IdentificationDecision Classify(IReadOnlyList<IdentificationMethodResult> methods)
    {
        if (methods.Any(m => m.Candidates.Count > 0))
            return IdentificationDecision.InsufficientEvidence;
        if (methods.Any(m => m.Status == IdentificationMethodStatus.Failed))
            return IdentificationDecision.MethodFailed;
        return methods.Any(m => m.Status == IdentificationMethodStatus.NothingFound)
            ? IdentificationDecision.NoMatch
            : IdentificationDecision.NoMethodRan;
    }

    private async Task<Outcome> ReconcileAsync(int fileId, Attempt attempt, CancellationToken cancellationToken)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is not { Status: FileStatus.Stored, TrackId: null })
            return SkipEligibility(fileId, attempt);

        attempt.FileName = file.OriginalFilename;
        var result = await IdentifyAsync(file, cancellationToken);
        attempt.Methods = result.Methods ?? [];
        var evidence = string.Join(", ", result.Evidence.Select(e => e.Source).Distinct());

        if (result.Status != IdentificationStatus.Identified || Normalize(result.Identity) is not { } identity)
        {
            var reason = result.UnresolvedReason ?? "the proposed identity has no title or no artist";
            logger.LogInformation(
                "File {FileId} ({Filename}) stays unlinked: {Reason} (evidence from: {Evidence}).",
                fileId, file.OriginalFilename, reason, evidence);
            attempt.Decision = result.Status == IdentificationStatus.Identified
                ? IdentificationDecision.InsufficientEvidence
                : Classify(attempt.Methods);
            attempt.Reason = reason;
            return Outcome.Unresolved;
        }

        await CatalogLock.WaitAsync(cancellationToken);
        try
        {
            // Re-read under the lock: the file may have been linked or deleted while it was being identified.
            var current = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
            if (current is not { Status: FileStatus.Stored, TrackId: null })
                return SkipEligibility(fileId, attempt);

            var track = await FindTrackAsync(identity, cancellationToken);
            var createdTrack = track is null;
            var linkEvidence = $"Credited with this identifier on a recording identified from: {evidence}";
            track ??= await CreateTrackAsync(identity, linkEvidence, cancellationToken);

            // One SaveChanges: the track, its artists and the link are stored together or not at all.
            current.Track = track;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "File {FileId} linked to {Kind} track {TrackId} \"{Title}\" (evidence from: {Evidence}).",
                fileId, createdTrack ? "new" : "existing", track.Id, track.Title, evidence);
            attempt.Decision = IdentificationDecision.Identified;
            attempt.TrackId = track.Id;
            attempt.Reason = $"Linked to {(createdTrack ? "new" : "existing")} track \"{track.Title}\".";
            return createdTrack ? Outcome.LinkedCreated : Outcome.LinkedExisting;
        }
        finally
        {
            CatalogLock.Release();
        }
    }

    private Outcome SkipEligibility(int fileId, Attempt attempt)
    {
        logger.LogInformation("File {FileId} skipped: it is no longer a stored file without a track.", fileId);
        attempt.Decision = IdentificationDecision.Skipped;
        attempt.Reason = "The file was no longer a stored file without a track when its turn came.";
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
        var artists = CatalogArtists.Normalize(identity.Artists);
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

    private async Task<Track> CreateTrackAsync(
        ProposedRecordingIdentity identity, string linkEvidence, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var track = new Track { Title = identity.Title, CreatedAt = now };
        foreach (var name in identity.Artists)
        {
            var artist = await ResolveArtistAsync(name, ReferenceFor(identity, name), linkEvidence, now, cancellationToken);
            track.Artists.Add(new TrackArtist { Artist = artist });
        }
        db.Tracks.Add(track);
        return track;
    }

    // The artist for a credited name. An identifier from the evidence decides first: the artist it is linked to. A name
    // alone is not evidence of identity, so an identifier nobody has is never linked to an existing artist because the
    // names match: it goes to a new, separate artist (merging stays a person's decision). Without an identifier the name
    // picks the oldest artist with that name, as it did before identifiers existed, and no link is made.
    private async Task<Artist> ResolveArtistAsync(
        string name, ArtistReference? reference, string linkEvidence, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (reference is not null && await links.FindLinkedArtistAsync(reference, cancellationToken) is { } linked)
            return linked;

        if (reference is null)
        {
            return await CatalogArtists.FindOrNewAsync(db, name, now, cancellationToken);
        }

        var artist = new Artist { Name = name, CreatedAt = now };
        await TryLinkAsync(artist, reference, linkEvidence, cancellationToken);
        return artist;
    }

    private async Task TryLinkAsync(
        Artist artist, ArtistReference reference, string linkEvidence, CancellationToken cancellationToken)
    {
        var outcome = await links.TryLinkAutomaticallyAsync(artist, reference, linkEvidence, cancellationToken);
        if (outcome is LinkCheck.Free or LinkCheck.AlreadyLinked)
            return;
        logger.LogInformation(
            "Artist \"{Name}\" was not linked to {Source} identifier {ExternalId}: {Outcome}.",
            artist.Name, reference.Source, reference.ExternalId, outcome);
    }

    private static ArtistReference? ReferenceFor(ProposedRecordingIdentity identity, string name) =>
        identity.ArtistReferences?.FirstOrDefault(r => string.Equals(r.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
}
