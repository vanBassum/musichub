namespace MusicHub.Api.Ingestion;

/// <summary>Binds the <c>FileIngestion</c> configuration section.</summary>
public sealed class FileIngestionOptions
{
    public const string SectionName = "FileIngestion";

    /// <summary>Maximum allowed upload size in bytes. Defaults to 500 MiB (524 288 000 bytes).</summary>
    public long MaxUploadSizeBytes { get; set; } = 524_288_000;

    /// <summary>
    /// Age after which a Pending or Uploading registration is considered stale. A starting default, not a
    /// promise that uploads finish in time: an upload still running in the process is never expired.
    /// </summary>
    public TimeSpan StaleUploadThreshold { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How often the stale-upload cleanup runs. Defaults to 10 minutes.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(10);
}
