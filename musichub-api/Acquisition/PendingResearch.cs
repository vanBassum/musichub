using MusicHub.Api.Catalog;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// A fresh search that is scheduled for a wanted track whose candidates ran out (for example every source was offline).
/// Owned by <see cref="AcquisitionFailover"/>. The row is the durable schedule, so a restart or a second instance neither
/// loses it nor searches twice: a worker claims a due row by raising <see cref="Searches"/>, which is the concurrency
/// token. The row goes away when a download is queued for the track, when the track has a file or is gone, or when
/// <see cref="DownloadWorkerOptions.ResearchMaxAttempts"/> searches found nothing; the track then stays wanted for a manual search.
/// </summary>
public class PendingResearch
{
    public const int ReasonMaxLength = AcquisitionDownload.ReasonMaxLength;

    public int Id { get; set; }

    public int TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>How many scheduled searches were claimed so far; also the concurrency token.</summary>
    public int Searches { get; set; }

    /// <summary>The earliest time a worker may claim it.</summary>
    public DateTimeOffset NextSearchAt { get; set; }

    /// <summary>A safe sentence about why the search is scheduled, never exception text.</summary>
    public string Reason { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
