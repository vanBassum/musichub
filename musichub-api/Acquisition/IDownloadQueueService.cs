namespace MusicHub.Api.Acquisition;

/// <summary>What to enqueue: the candidate to download for a track, and who asked.</summary>
/// <param name="JobExecutionId">The job execution that enqueues it, as provenance; null outside a job.</param>
/// <param name="SearchId">The search that found the candidate, to correlate it in the track's activity; null when unknown.</param>
public sealed record EnqueueDownloadCommand(
    int TrackId, AcquisitionCandidate Candidate, string? JobExecutionId = null, Guid? SearchId = null);

public enum EnqueueOutcome
{
    /// <summary>A new request was queued.</summary>
    Created,

    /// <summary>The track or the candidate already has an active request; nothing was added and that request is returned.</summary>
    AlreadyActive,

    /// <summary>The track does not exist; nothing was added.</summary>
    TrackNotFound,
}

public sealed record EnqueueResult(EnqueueOutcome Outcome, DownloadRequestView? Request);

public enum RetryOutcome
{
    /// <summary>The failed request is queued again.</summary>
    Retried,

    /// <summary>There is no such visible request.</summary>
    NotFound,

    /// <summary>The request is not Failed (any more); nothing changed.</summary>
    NotFailed,

    /// <summary>The track or the candidate has another active request; nothing changed.</summary>
    AlreadyActive,
}

public enum RemoveOutcome
{
    /// <summary>The request left the visible queue.</summary>
    Removed,

    /// <summary>There is no such visible request (also: it was removed already).</summary>
    NotFound,

    /// <summary>The request is active (or became active); nothing changed.</summary>
    NotRemovable,
}

/// <summary>The request as it is now, for the caller to report; null when it is not visible.</summary>
public sealed record RetryResult(RetryOutcome Outcome, DownloadRequestView? Request);

/// <summary>A request as a worker needs it: what to download and which attempt this is.</summary>
public sealed record ClaimedDownload(int RequestId, int? TrackId, AcquisitionCandidate Candidate, int AttemptNumber);

/// <summary>A request as the API shows it. It names the provider and the file, never the source or the remote path.</summary>
public sealed record DownloadRequestView(
    int Id,
    int? TrackId,
    string? TrackTitle,
    IReadOnlyList<string> Artists,
    string Provider,
    string FileName,
    long? SizeBytes,
    int? BitrateKbps,
    string? Format,
    DownloadRequestStatus Status,
    int AttemptCount,
    long BytesTransferred,
    int? ProgressPercent,
    DateTimeOffset? NextAttemptAt,
    AcquisitionFailure? Failure,
    string? Error,
    string? JobExecutionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record DownloadRequestPage(IReadOnlyList<DownloadRequestView> Items, int Total, int Page, int PageSize);

/// <summary>
/// The durable download queue. The search job enqueues, the worker claims and reports, the Downloads page reads. It
/// executes no download and decides nothing about search or import. Every transition method returns false when the
/// request is missing or not in a status that allows it (or another caller moved it first): the caller then drops the
/// work instead of overwriting the other's.
/// </summary>
public interface IDownloadQueueService
{
    /// <summary>
    /// Queues the candidate for the track unless the track or the candidate already has an active request, so repeating
    /// a call, also concurrently, is safe and returns the existing request.
    /// </summary>
    Task<EnqueueResult> EnqueueAsync(EnqueueDownloadCommand command, CancellationToken ct);

    Task<DownloadRequestView?> GetAsync(int id, CancellationToken ct);

    /// <summary>A page of requests, newest first; all of them, or only those in <paramref name="status"/>.</summary>
    Task<DownloadRequestPage> ListAsync(DownloadRequestStatus? status, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// The user's manual retry (Failed to Queued, attempts counted afresh): the worker picks it up like any queued
    /// request. Repeating it, also concurrently, queues it once; the other calls find it no longer Failed.
    /// </summary>
    Task<RetryResult> RetryAsync(int id, CancellationToken ct);

    /// <summary>
    /// Takes a Failed or Completed request out of the visible queue. Only the queue entry goes: the wishlist, the catalog,
    /// the stored files and the attempt history stay. An active request is never removed.
    /// </summary>
    Task<RemoveOutcome> RemoveAsync(int id, CancellationToken ct);

    /// <summary>Takes the oldest request that may start now (Queued to Downloading), or null when there is none.</summary>
    Task<ClaimedDownload?> ClaimNextAsync(CancellationToken ct);

    /// <summary>Records bytes received while Downloading; not a status change.</summary>
    Task<bool> ReportProgressAsync(int id, long bytesTransferred, CancellationToken ct);

    /// <summary>The bytes arrived (Downloading to Downloaded); <paramref name="attemptId"/> is the history record of the attempt.</summary>
    Task<bool> MarkDownloadedAsync(int id, int? attemptId, CancellationToken ct);

    /// <summary>The handoff to identification and import is done (Downloaded to Completed).</summary>
    Task<bool> MarkCompletedAsync(int id, CancellationToken ct);

    /// <summary>The attempt failed and another will follow not before <paramref name="notBefore"/> (Downloading to Queued).</summary>
    Task<bool> RetryLaterAsync(
        int id, AcquisitionFailure failure, string error, DateTimeOffset notBefore, int? attemptId, CancellationToken ct);

    /// <summary>No more attempts (Queued, Downloading or Downloaded to Failed). <paramref name="error"/> is a safe sentence.</summary>
    Task<bool> FailAsync(int id, AcquisitionFailure failure, string error, int? attemptId, CancellationToken ct);

    /// <summary>
    /// Restart recovery: puts every request still Downloading back in the queue, because no worker owns it any more.
    /// Run it once when the worker starts, before it claims. Returns how many were requeued.
    /// </summary>
    Task<int> RequeueInterruptedAsync(CancellationToken ct);
}
