using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Storage;

namespace MusicHub.Api.Catalog;

/// <summary>What one reconciliation run did. Files that stayed unlinked (unresolved or failed) are unchanged.</summary>
/// <param name="Examined">Files the run attempted: Stored files without a track, and with re-identification all Stored files.</param>
/// <param name="Linked">Files that had no track and are now linked to one.</param>
/// <param name="TracksCreated">Of the links and replacements, the tracks that were created for them (the others reused an existing track).</param>
/// <param name="Unresolved">Unlinked files the identification could not establish an identity for.</param>
/// <param name="Skipped">Files that were no longer eligible when their turn came (deleted, linked or changed meanwhile).</param>
/// <param name="Failed">Files whose processing failed (storage, identification or database); they stay as they were.</param>
/// <param name="Replaced">Linked files whose automatic link was replaced by another track. Only with re-identification.</param>
/// <param name="Retained">Linked files that kept their link: the identification confirmed it, was inconclusive, or a person had confirmed it. Only with re-identification.</param>
public sealed record CatalogReconciliationSummary(
    int Examined, int Linked, int TracksCreated, int Unresolved, int Skipped, int Failed,
    int Replaced = 0, int Retained = 0);

/// <summary>
/// Links MusicHub-managed Stored files that have no catalog track to the catalog, using the existing
/// <see cref="IRecordingIdentifier"/> and its confidence policy. A file whose identity is established is linked to the
/// existing track with the same title and the same artists (and no version), or to a new track; an unresolved file is
/// left exactly as it was. One file's failure never stops the others, and a rerun finds only what is still unlinked, so
/// it creates no duplicate tracks, artists or links. The proposed identity carries no version, so a track with a
/// version is never reused for it: versions stay distinct tracks.
/// With re-identification, files that already have a track go through the same identification and decision as the
/// others. Their current link stays unless the identification justifies another track, and then only an automatic link
/// is replaced: a person's confirmation is never changed, an inconclusive result never unlinks a file, and every
/// attempt is recorded with its evidence.
/// </summary>
public sealed class CatalogReconciliation(
    MusicHubDbContext db,
    IFileStorage storage,
    IRecordingIdentifier identifier,
    TimeProvider time,
    ILogger<CatalogReconciliation> logger)
{
    // Finding and creating tracks and artists must not interleave between runs in this process (the job itself is
    // exclusive across processes). Adding to the library creates tracks too, so it takes the same lock.
    internal static readonly SemaphoreSlim CatalogLock = new(1, 1);

    private readonly ArtistExternalLinks links = new(db, time);

    /// <param name="executionId">The Hangfire execution the per-file attempts are recorded under; null when the run is not a job.</param>
    /// <param name="reIdentifyAll">Also identify files that already have a track; only an automatic link can be replaced.</param>
    public async Task<CatalogReconciliationSummary> RunAsync(
        string? executionId, CancellationToken cancellationToken, bool reIdentifyAll = false)
    {
        var ids = await db.Files.AsNoTracking()
            .Where(f => f.Status == FileStatus.Stored && (reIdentifyAll || f.TrackId == null))
            .OrderBy(f => f.Id)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        logger.LogInformation(
            "Catalog reconciliation started: {Count} stored files {Scope}.",
            ids.Count, reIdentifyAll ? "to identify again, linked or not" : "without a track");

        int linked = 0, created = 0, unresolved = 0, skipped = 0, failed = 0, replaced = 0, retained = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attempt = new Attempt();
            try
            {
                switch (await ReconcileAsync(id, executionId ?? "", reIdentifyAll, attempt, cancellationToken))
                {
                    case Outcome.Linked:
                        linked++;
                        break;
                    case Outcome.Replaced:
                        replaced++;
                        break;
                    case Outcome.Retained:
                        retained++;
                        break;
                    case Outcome.Unresolved:
                        unresolved++;
                        break;
                    default:
                        skipped++;
                        break;
                }
                if (attempt.CreatedTrack)
                    created++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed++;
                logger.LogError(ex, "Reconciling file {FileId} failed; it stays as it was.", id);
                // Only the exception type: the message may carry storage or database details.
                attempt.Decision = IdentificationDecision.Error;
                attempt.TrackId = null;
                attempt.Reason = $"Processing failed ({ex.GetType().Name}); the file stays as it was.";
            }
            finally
            {
                db.ChangeTracker.Clear();
            }

            if (!attempt.Recorded)
                await RecordAsync(executionId ?? "", id, attempt, cancellationToken);
        }

        var summary = new CatalogReconciliationSummary(
            ids.Count, linked, created, unresolved, skipped, failed, replaced, retained);
        logger.LogInformation(
            "Catalog reconciliation finished: {Examined} examined, {Linked} linked ({Created} new tracks), {Unresolved} unresolved, {Skipped} skipped, {Failed} failed, {Replaced} replaced, {Retained} retained.",
            summary.Examined, summary.Linked, summary.TracksCreated, summary.Unresolved, summary.Skipped, summary.Failed,
            summary.Replaced, summary.Retained);
        return summary;
    }

    private enum Outcome { Linked, Replaced, Retained, Unresolved, Skipped }

    /// <summary>What one file's attempt turned out to be; filled in as the attempt proceeds, recorded once at the end.</summary>
    private sealed class Attempt
    {
        public string FileName { get; set; } = "";
        public IdentificationDecision Decision { get; set; } = IdentificationDecision.Error;
        public string Reason { get; set; } = "The attempt did not finish.";
        public int? TrackId { get; set; }

        /// <summary>True when the attempt created the track its link points to.</summary>
        public bool CreatedTrack { get; set; }

        public IReadOnlyList<IdentificationMethodResult> Methods { get; set; } = [];

        /// <summary>True once the attempt is stored; a linked file stores it together with the link.</summary>
        public bool Recorded { get; set; }
    }

    private IdentificationOutcome ToOutcome(string executionId, int fileId, Attempt attempt)
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
            PipelineVersion = BuildVersion.Current,
        };
        outcome.SetMethods(attempt.Methods);
        return outcome;
    }

    // The diagnostic must never fail the file: it only describes what happened.
    private async Task RecordAsync(string executionId, int fileId, Attempt attempt, CancellationToken cancellationToken)
    {
        try
        {
            db.IdentificationOutcomes.Add(ToOutcome(executionId, fileId, attempt));
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

    private async Task<Outcome> ReconcileAsync(
        int fileId, string executionId, bool reIdentifyAll, Attempt attempt, CancellationToken cancellationToken)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is not { Status: FileStatus.Stored } || (file.TrackId is not null && !reIdentifyAll))
            return SkipEligibility(fileId, attempt);

        attempt.FileName = file.OriginalFilename;
        var result = await IdentifyAsync(file, cancellationToken);
        attempt.Methods = result.Methods ?? [];
        var evidence = string.Join(", ", result.Evidence.Select(e => e.Source).Distinct());

        if (result.Status != IdentificationStatus.Identified || Normalize(result.Identity) is not { } identity)
        {
            var reason = result.UnresolvedReason ?? "the proposed identity has no title or no artist";
            logger.LogInformation(
                "File {FileId} ({Filename}) has no new identity: {Reason} (evidence from: {Evidence}).",
                fileId, file.OriginalFilename, reason, evidence);
            attempt.Decision = result.Status == IdentificationStatus.Identified
                ? IdentificationDecision.InsufficientEvidence
                : IdentificationDecisions.Classify(attempt.Methods);
            if (file.TrackId is null)
            {
                attempt.Reason = reason;
                return Outcome.Unresolved;
            }

            attempt.Reason = $"{reason}; the link to its track was kept.";
            return Outcome.Retained;
        }

        await CatalogLock.WaitAsync(cancellationToken);
        try
        {
            // Re-read under the lock: the file may have been linked, relinked or deleted while it was being identified.
            var current = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
            if (current is not { Status: FileStatus.Stored } || current.TrackId != file.TrackId)
                return SkipEligibility(fileId, attempt);

            var previousTrackId = current.TrackId;
            var track = await FindTrackAsync(identity, cancellationToken);
            if (previousTrackId is not null && track?.Id == previousTrackId)
                return Retain(attempt, previousTrackId.Value, $"Identified again as its track \"{track.Title}\"; the link stays.");
            if (previousTrackId is not null && await IsManuallyLinkedAsync(fileId, cancellationToken))
                return Retain(attempt, previousTrackId.Value,
                    $"Identified as \"{identity.Title}\" by {string.Join(", ", identity.Artists)}, which contradicts the identity a person confirmed; the confirmed link stays.");

            attempt.CreatedTrack = track is null;
            var linkEvidence = $"Credited with this identifier on a recording identified from: {evidence}";
            track ??= await CreateTrackAsync(identity, linkEvidence, cancellationToken);

            // One transaction: the track, its artists, the link, the attempt and the applied decision are stored
            // together or not at all.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            current.Track = track;
            await db.SaveChangesAsync(cancellationToken);

            var kind = attempt.CreatedTrack ? "new" : "existing";
            attempt.Decision = IdentificationDecision.Identified;
            attempt.TrackId = track.Id;
            attempt.Reason = previousTrackId is null
                ? $"Linked to {kind} track \"{track.Title}\"."
                : $"Identified as {kind} track \"{track.Title}\"; it replaces the automatic link to track {previousTrackId}.";
            var outcome = ToOutcome(executionId, fileId, attempt);
            db.IdentificationOutcomes.Add(outcome);
            db.AppliedIdentifications.Add(new AppliedIdentification
            {
                FileId = fileId,
                TrackId = track.Id,
                Source = IdentificationSource.Automatic,
                Attempt = outcome,
                AppliedAt = outcome.RecordedAt,
            });
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            attempt.Recorded = true;

            logger.LogInformation(
                "File {FileId} {Action} {Kind} track {TrackId} \"{Title}\" (evidence from: {Evidence}).",
                fileId, previousTrackId is null ? "linked to" : "relinked to", kind, track.Id, track.Title, evidence);
            return previousTrackId is null ? Outcome.Linked : Outcome.Replaced;
        }
        finally
        {
            CatalogLock.Release();
        }
    }

    // The attempt names the identity the engine established and the link that stays; the file does not change.
    private static Outcome Retain(Attempt attempt, int trackId, string reason)
    {
        attempt.Decision = IdentificationDecision.Identified;
        attempt.TrackId = trackId;
        attempt.Reason = reason;
        return Outcome.Retained;
    }

    // A person's confirmation is the file's latest applied identification; automation never replaces it.
    private Task<bool> IsManuallyLinkedAsync(int fileId, CancellationToken cancellationToken) =>
        db.AppliedIdentifications.AsNoTracking()
            .Where(a => a.FileId == fileId)
            .OrderByDescending(a => a.Id)
            .Select(a => a.Source == IdentificationSource.Manual)
            .FirstOrDefaultAsync(cancellationToken);

    private Outcome SkipEligibility(int fileId, Attempt attempt)
    {
        logger.LogInformation("File {FileId} skipped: it is no longer eligible.", fileId);
        attempt.Decision = IdentificationDecision.Skipped;
        attempt.Reason = "The file was no longer eligible when its turn came.";
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

        return candidates.FirstOrDefault(t => CreditsExactly(t, identity.Artists));
    }

    private static bool CreditsExactly(Track track, IEnumerable<string> artists) =>
        track.Artists.Select(a => a.Artist.Name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(artists.ToHashSet(StringComparer.OrdinalIgnoreCase));

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
