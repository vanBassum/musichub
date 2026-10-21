using System.Text.Json.Serialization;
using MusicHub.Api.Catalog;

namespace MusicHub.Api.Acquisition;

/// <summary>Where a queued download stands. Queued, Downloading and Downloaded are active; Completed and Failed are final.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DownloadRequestStatus
{
    /// <summary>Waiting for the worker (also after a failed attempt that will be retried, or an interrupted one).</summary>
    Queued,

    /// <summary>The worker claimed it and the transfer is under way.</summary>
    Downloading,

    /// <summary>The bytes arrived and wait for (or are in) identification and import.</summary>
    Downloaded,

    /// <summary>The handoff to identification and import is done; the request has nothing left to do.</summary>
    Completed,

    /// <summary>No more attempts will be made; <see cref="DownloadRequest.Failure"/> and <see cref="DownloadRequest.Error"/> say why.</summary>
    Failed,
}

/// <summary>The one owner of which statuses are active and which transitions are allowed.</summary>
public static class DownloadRequestStatuses
{
    /// <summary>The statuses in which a request owns its track and its source: at most one request per each may be in them.</summary>
    public static readonly IReadOnlyList<DownloadRequestStatus> Active =
        [DownloadRequestStatus.Queued, DownloadRequestStatus.Downloading, DownloadRequestStatus.Downloaded];

    public static bool IsActive(DownloadRequestStatus status) => Active.Contains(status);

    /// <summary>Downloading to Queued is a retry or the recovery of an interrupted transfer. Final statuses never change.</summary>
    public static bool CanTransition(DownloadRequestStatus from, DownloadRequestStatus to) => (from, to) switch
    {
        (DownloadRequestStatus.Queued, DownloadRequestStatus.Downloading) => true,
        (DownloadRequestStatus.Queued, DownloadRequestStatus.Failed) => true,
        (DownloadRequestStatus.Downloading, DownloadRequestStatus.Queued) => true,
        (DownloadRequestStatus.Downloading, DownloadRequestStatus.Downloaded) => true,
        (DownloadRequestStatus.Downloading, DownloadRequestStatus.Failed) => true,
        (DownloadRequestStatus.Downloaded, DownloadRequestStatus.Completed) => true,
        (DownloadRequestStatus.Downloaded, DownloadRequestStatus.Failed) => true,
        _ => false,
    };
}

/// <summary>
/// MusicHub's durable intent to download one candidate for one track, owned by <see cref="DownloadQueueService"/>.
/// The track and the candidate's provider, source and path are its identity; they are provenance and never change.
/// slskd (or any provider) only performs the transfer. <see cref="AcquisitionDownload"/> stays the permanent record of
/// each attempt's outcome; this row is the queue entry that points at it.
/// </summary>
public class DownloadRequest
{
    public const int ErrorMaxLength = AcquisitionDownload.ReasonMaxLength;
    public const int FormatMaxLength = 20;

    public int Id { get; set; }

    /// <summary>The track this download is meant to supply; null once that track was deleted.</summary>
    public int? TrackId { get; set; }
    public Track? Track { get; set; }

    public required string Provider { get; set; }
    public required string Source { get; set; }
    public required string RemotePath { get; set; }

    /// <summary>What the candidate announced when it was chosen; hints, never an identity.</summary>
    public long? SizeBytes { get; set; }
    public int? BitrateKbps { get; set; }
    public string? Format { get; set; }

    /// <summary>The job execution that enqueued it; null when it was enqueued outside a job.</summary>
    public string? JobExecutionId { get; set; }

    /// <summary>Also the concurrency token: a transition only applies to the status it was read in.</summary>
    public DownloadRequestStatus Status { get; set; }

    /// <summary>How many times a worker claimed it.</summary>
    public int AttemptCount { get; set; }

    public long BytesTransferred { get; set; }

    /// <summary>For a queued request, the earliest time a worker may claim it again; null means now.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>The latest failed attempt's reason, kept while the request waits to be retried and when it failed for good.</summary>
    public AcquisitionFailure? Failure { get; set; }

    /// <summary>A safe sentence about the latest failure, never exception text.</summary>
    public string? Error { get; set; }

    /// <summary>The permanent history record of the latest attempt; null before an attempt or once that record is gone.</summary>
    public int? AcquisitionDownloadId { get; set; }
    public AcquisitionDownload? AcquisitionDownload { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When the first attempt started.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it reached Completed or Failed.</summary>
    public DateTimeOffset? FinishedAt { get; set; }
}
