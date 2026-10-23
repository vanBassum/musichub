using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// The one owner of the durable download history: it records each attempt as it progresses and answers whether a
/// candidate was rejected before. It decides nothing about acquisition itself; the caller tells it what happened.
/// </summary>
public class AcquisitionHistory(MusicHubDbContext db, TimeProvider time)
{
    public async Task<AcquisitionDownload> BeginAsync(
        AcquisitionCandidate candidate, int? intendedTrackId, CancellationToken ct, string? jobExecutionId = null)
    {
        var (provider, source, remotePath) = StoredIdentity(candidate);
        var download = new AcquisitionDownload
        {
            Provider = provider,
            Source = source,
            RemotePath = remotePath,
            SizeBytes = candidate.SizeBytes,
            BitrateKbps = candidate.BitrateKbps,
            JobExecutionId = jobExecutionId,
            IntendedTrackId = intendedTrackId,
            StartedAt = time.GetUtcNow(),
            State = AcquisitionDownloadState.Started,
        };
        db.AcquisitionDownloads.Add(download);
        await db.SaveChangesAsync(ct);
        return download;
    }

    public Task DownloadedAsync(AcquisitionDownload download, DownloadHashes hashes, CancellationToken ct)
    {
        download.State = AcquisitionDownloadState.Downloaded;
        download.ContentHash = hashes.ContentHash;
        download.AudioHash = hashes.Audio.Hash;
        download.AudioHashStatus = hashes.Audio.Status;
        download.AudioHashVersion = Mp3AudioPayload.Version;
        download.FinishedAt = time.GetUtcNow();
        return db.SaveChangesAsync(ct);
    }

    /// <summary>The run was cancelled during the transfer; nothing was judged, so the source may be tried again.</summary>
    public Task CancelledAsync(AcquisitionDownload download, string reason, CancellationToken ct)
    {
        download.State = AcquisitionDownloadState.Cancelled;
        download.Reason = Truncate(reason, AcquisitionDownload.ReasonMaxLength);
        download.FinishedAt = time.GetUtcNow();
        return db.SaveChangesAsync(ct);
    }

    public Task FailedAsync(AcquisitionDownload download, AcquisitionFailure failure, string reason, CancellationToken ct)
    {
        download.State = AcquisitionDownloadState.Failed;
        download.Failure = failure;
        download.Reason = Truncate(reason, AcquisitionDownload.ReasonMaxLength);
        download.FinishedAt = time.GetUtcNow();
        return db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The file was imported as <paramref name="fileId"/>, an existing canonical file record. The identification
    /// <paramref name="attempt"/> that led to it is stored together with the decision.
    /// </summary>
    public Task AcceptedAsync(
        AcquisitionDownload download, int fileId, string reason, CancellationToken ct, IdentificationOutcome? attempt = null)
    {
        download.FileId = fileId;
        return DecideAsync(download, AcquisitionOutcome.Accepted, reason, attempt, ct);
    }

    /// <summary>The file stays outside the library; the identification <paramref name="attempt"/> that led to it is stored with the decision.</summary>
    public Task RejectedAsync(
        AcquisitionDownload download, string reason, CancellationToken ct, IdentificationOutcome? attempt = null) =>
        DecideAsync(download, AcquisitionOutcome.Rejected, reason, attempt, ct);

    /// <summary>
    /// One identification attempt on the bytes of <paramref name="download"/>, not yet stored: it is stored by
    /// <see cref="AcceptedAsync"/> or <see cref="RejectedAsync"/>, with the decision it led to.
    /// </summary>
    public IdentificationOutcome AttemptOn(
        AcquisitionDownload download, string fileName, IdentificationDecision decision, string reason,
        IReadOnlyList<IdentificationMethodResult> methods, int? trackId)
    {
        var attempt = new IdentificationOutcome
        {
            DownloadId = download.Id,
            FileName = IdentificationOutcome.Truncate(fileName, IdentificationOutcome.FileNameMaxLength),
            Decision = decision,
            Reason = IdentificationOutcome.Truncate(reason, IdentificationOutcome.ReasonMaxLength),
            TrackId = trackId,
            RecordedAt = time.GetUtcNow(),
            PipelineVersion = BuildVersion.Current,
        };
        attempt.SetMethods(methods);
        return attempt;
    }

    /// <summary>
    /// Whether this source was rejected before: the same provider, source and path. Checked before downloading, so a
    /// rejected candidate is not fetched again. It says nothing about files that merely look alike.
    /// </summary>
    public Task<bool> WasRejectedAsync(AcquisitionCandidate candidate, CancellationToken ct)
    {
        var (provider, source, remotePath) = StoredIdentity(candidate);
        return db.AcquisitionDownloads.AnyAsync(d =>
            d.Outcome == AcquisitionOutcome.Rejected
            && d.Provider == provider
            && d.Source == source
            && d.RemotePath == remotePath, ct);
    }

    /// <summary>
    /// Whether the provider confirmed this candidate's source offline since <paramref name="since"/>. The source, not
    /// the file, is what is offline, so any file of that source counts.
    /// </summary>
    public async Task<bool> WasSourceOfflineSinceAsync(AcquisitionCandidate candidate, DateTimeOffset since, CancellationToken ct)
    {
        var (provider, source, _) = StoredIdentity(candidate);
        // Compared in memory: the time is a time zone aware value that not every database can compare.
        var finished = await db.AcquisitionDownloads
            .Where(d => d.Failure == AcquisitionFailure.SourceOffline && d.Provider == provider && d.Source == source)
            .Select(d => d.FinishedAt)
            .ToListAsync(ct);
        return finished.Any(at => at >= since);
    }

    /// <summary>
    /// Whether byte-identical content was rejected before, for a file that is already downloaded (a hash is only
    /// known then). Identical bytes are not proof of the same recording, so this is a signal, not an identity. With
    /// <paramref name="intendedTrackId"/> only a rejection for that track counts: equal bytes say nothing about any
    /// other track.
    /// </summary>
    public Task<bool> WasContentRejectedAsync(string contentHash, CancellationToken ct, int? intendedTrackId = null) =>
        db.AcquisitionDownloads.AnyAsync(d =>
            d.Outcome == AcquisitionOutcome.Rejected && d.ContentHash == contentHash
            && (intendedTrackId == null || d.IntendedTrackId == intendedTrackId), ct);

    /// <summary>
    /// Whether the same encoded audio (tags aside) was rejected before. Only hashes of the same
    /// <see cref="Mp3AudioPayload.Version"/> are comparable. A signal, not a recording identity.
    /// </summary>
    public Task<bool> WasAudioRejectedAsync(string audioHash, CancellationToken ct) =>
        db.AcquisitionDownloads.AnyAsync(d =>
            d.Outcome == AcquisitionOutcome.Rejected
            && d.AudioHash == audioHash
            && d.AudioHashVersion == Mp3AudioPayload.Version, ct);

    private Task DecideAsync(
        AcquisitionDownload download, AcquisitionOutcome outcome, string reason, IdentificationOutcome? attempt, CancellationToken ct)
    {
        if (attempt is not null)
            db.IdentificationOutcomes.Add(attempt);
        download.Outcome = outcome;
        download.Reason = Truncate(reason, AcquisitionDownload.ReasonMaxLength);
        download.DecidedAt = time.GetUtcNow();
        return db.SaveChangesAsync(ct);
    }

    /// <summary>A candidate's identity as stored (cut to the column lengths), for both writing and matching.</summary>
    internal static (string Provider, string Source, string RemotePath) StoredIdentity(AcquisitionCandidate candidate) => (
        Truncate(candidate.Provider, AcquisitionDownload.ProviderMaxLength),
        Truncate(candidate.Source, AcquisitionDownload.SourceMaxLength),
        Truncate(candidate.Path, AcquisitionDownload.RemotePathMaxLength));

    internal static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
