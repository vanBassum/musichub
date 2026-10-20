using System.Text.Json.Serialization;
using MusicHub.Api.Catalog;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Acquisition;

/// <summary>How a download attempt stands. Only Imported put a file in the library.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DownloadStatus
{
    /// <summary>The transfer was started and has not ended yet (or the process stopped before it was recorded).</summary>
    Downloading,

    /// <summary>No file arrived (the provider or the source failed). Nothing was judged, so the candidate may be tried again.</summary>
    Failed,

    /// <summary>The run was cancelled while the transfer was going. Nothing was judged either.</summary>
    Cancelled,

    /// <summary>A file arrived and was not accepted as the wanted recording. It stays outside the library and is not downloaded again.</summary>
    Rejected,

    /// <summary>A file arrived, was identified as the wanted recording and was imported (<see cref="DownloadHistory.FileId"/>).</summary>
    Imported,
}

/// <summary>
/// The durable record of one attempted candidate download: where it came from, how it ended and why, and the hashes of
/// the bytes that arrived. Kept for successes too. Its identification attempts are <see cref="IdentificationOutcome"/>
/// rows that point here (<see cref="IdentificationOutcome.DownloadId"/>); the library file of an import is
/// <see cref="FileId"/>. Searches and their result lists are transient and never stored.
/// </summary>
public class DownloadHistory
{
    public const int SourceMaxLength = 200;
    public const int PathMaxLength = 1000;
    public const int ReasonMaxLength = 2000;
    public const int HashAlgorithmMaxLength = 50;

    public int Id { get; set; }

    /// <summary>The track the download was for; null once that track was deleted.</summary>
    public int? TrackId { get; set; }
    public Track? Track { get; set; }

    /// <summary>The provider's name, for example <c>slskd</c>.</summary>
    public string Provider { get; set; } = "";

    /// <summary>Who offered the file, as the provider names it (for example a peer).</summary>
    public string Source { get; set; } = "";

    /// <summary>The file's path at the source, exactly as the provider gave it.</summary>
    public string SourcePath { get; set; } = "";

    /// <summary>The size the source announced.</summary>
    public long SizeBytes { get; set; }

    public int? BitrateKbps { get; set; }

    public DownloadStatus Status { get; set; }

    /// <summary>Why it ended as it did (the failure, the rejection, or what was imported). Never holds a secret.</summary>
    public string Reason { get; set; } = "";

    /// <summary>SHA-256 (lowercase hex) of the whole file as downloaded; null until bytes were available.</summary>
    public string? FileSha256 { get; set; }

    /// <summary>SHA-256 of the MP3 audio payload, independent of tags; null when the file had no audio payload to hash.</summary>
    public string? AudioSha256 { get; set; }

    /// <summary>How <see cref="AudioSha256"/> was extracted (<see cref="AudioHashing.Mp3PayloadAlgorithm"/>), so it stays reproducible.</summary>
    public string? AudioHashAlgorithm { get; set; }

    /// <summary>The library file this download became; null unless Imported, and again once that file was deleted.</summary>
    public int? FileId { get; set; }
    public FileRecord? File { get; set; }

    /// <summary>The Hangfire execution that made the attempt.</summary>
    public string JobExecutionId { get; set; } = "";

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
