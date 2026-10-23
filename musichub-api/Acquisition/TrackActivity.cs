using System.Text.Json.Serialization;
using MusicHub.Api.Catalog;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// A milestone in acquiring one wanted track. Each kind is written where the transition really happens (the search, the
/// queue, the history of a download's outcome); none is derived or guessed afterwards.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrackActivityKind
{
    SearchStarted,

    /// <summary>The search returned; the counts say how many results, plausible MP3s and eligible candidates there were.</summary>
    SearchCompleted,

    /// <summary>The search returned nothing worth downloading: no plausible MP3, or only candidates rejected before.</summary>
    NotFound,

    /// <summary>The search could not run or failed; the track's state is unknown.</summary>
    SearchFailed,

    DownloadQueued,
    DownloadStarted,

    /// <summary>The bytes arrived. Identifying and importing them is a separate step.</summary>
    DownloadCompleted,

    /// <summary>An attempt failed and another one is scheduled.</summary>
    DownloadRetryScheduled,

    /// <summary>The transfer was interrupted by a restart and queued again.</summary>
    DownloadInterrupted,

    /// <summary>No more attempts will be made.</summary>
    DownloadFailed,

    /// <summary>The user queued a failed download again.</summary>
    DownloadRetriedManually,

    /// <summary>The downloaded file was identified as the wanted recording and imported.</summary>
    ImportAccepted,

    /// <summary>The downloaded file was not accepted as the wanted recording; the source is not downloaded again.</summary>
    ImportRejected,
}

/// <summary>
/// One persisted, immutable event in a track's acquisition history, so the history survives restarts and attempts.
/// Written through <see cref="TrackActivityLog"/> only. Correlation: <see cref="SearchId"/> ties the events of one
/// search (and the request it queued) together, <see cref="DownloadRequestId"/> and <see cref="AcquisitionDownloadId"/>
/// tie the events of one request and one attempt, <see cref="JobExecutionId"/> names the job run. Search results are not
/// stored (only their counts); text never carries a secret, a path or a remote address.
/// </summary>
public class TrackActivity
{
    public const int SummaryMaxLength = 500;
    public const int DetailMaxLength = 1000;

    public long Id { get; set; }

    /// <summary>The track this event is about; the events go with the track when it is deleted.</summary>
    public int TrackId { get; set; }
    public Track Track { get; set; } = null!;

    public TrackActivityKind Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>One user-readable sentence, sanitized.</summary>
    public required string Summary { get; set; }

    /// <summary>More technical text, shown on demand (an error's safe sentence); sanitized.</summary>
    public string? Detail { get; set; }

    public Guid? SearchId { get; set; }
    public int? DownloadRequestId { get; set; }
    public int? AcquisitionDownloadId { get; set; }
    public string? JobExecutionId { get; set; }

    /// <summary>The download attempt number of the request, for download events.</summary>
    public int? AttemptNumber { get; set; }

    /// <summary>What the provider returned (search events).</summary>
    public int? ResultCount { get; set; }

    /// <summary>The MP3s that plausibly are the wanted recording (search events).</summary>
    public int? PlausibleCount { get; set; }

    /// <summary>The plausible candidates not rejected before: those that may be downloaded (search events).</summary>
    public int? EligibleCount { get; set; }
}
