using System.Text.Json.Serialization;
using MusicHub.Api.Catalog;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Acquisition;

/// <summary>How far one download attempt got.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AcquisitionDownloadState
{
    /// <summary>The attempt started; no result is known yet.</summary>
    Started,

    /// <summary>The bytes arrived (and were hashed).</summary>
    Downloaded,

    /// <summary>No file arrived; <see cref="AcquisitionDownload.Failure"/> says why.</summary>
    Failed,
}

/// <summary>What was decided about the downloaded file.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AcquisitionOutcome
{
    /// <summary>Not decided yet: the download is under way, failed, or waits for identification.</summary>
    Undecided,

    /// <summary>The file was imported; <see cref="AcquisitionDownload.FileId"/> is its canonical record.</summary>
    Accepted,

    /// <summary>The file was not imported; the source is not downloaded again for the same reason.</summary>
    Rejected,
}

/// <summary>
/// A permanent record of one download attempt of an acquisition candidate, accepted or rejected. It outlives the
/// temporary file. Search queries and listings are not stored; only what was actually attempted is.
/// <see cref="Provider"/>, <see cref="Source"/> and <see cref="RemotePath"/> are the source's identity: who offered
/// what, as the provider named it. <see cref="ContentHash"/> is the verified identity of the bytes, known only after
/// the download. Neither says which recording the file is: identification decides that.
/// </summary>
public class AcquisitionDownload
{
    public const int ProviderMaxLength = 50;
    public const int SourceMaxLength = 200;
    public const int RemotePathMaxLength = 1000;
    public const int ReasonMaxLength = 1000;

    public int Id { get; set; }

    public required string Provider { get; set; }
    public required string Source { get; set; }
    public required string RemotePath { get; set; }

    /// <summary>The track this download was meant to supply; null once that track was deleted.</summary>
    public int? IntendedTrackId { get; set; }
    public Track? IntendedTrack { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the download ended, in either state; null while it runs.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    public AcquisitionDownloadState State { get; set; }

    /// <summary>Why no file arrived; set only for <see cref="AcquisitionDownloadState.Failed"/>.</summary>
    public AcquisitionFailure? Failure { get; set; }

    public AcquisitionOutcome Outcome { get; set; }

    /// <summary>Why the file was accepted or rejected, or what went wrong. A safe sentence, never exception text.</summary>
    public string? Reason { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>SHA-256 of the downloaded bytes, lowercase hex; kept when the temporary file is deleted.</summary>
    public string? ContentHash { get; set; }

    /// <summary>The canonical file record of an accepted download; null otherwise or once that file was deleted.</summary>
    public int? FileId { get; set; }
    public FileRecord? File { get; set; }
}
