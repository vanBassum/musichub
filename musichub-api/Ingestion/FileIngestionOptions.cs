namespace MusicHub.Api.Ingestion;

/// <summary>Binds the <c>FileIngestion</c> configuration section.</summary>
public sealed class FileIngestionOptions
{
    public const string SectionName = "FileIngestion";

    /// <summary>Maximum allowed upload size in bytes. Defaults to 500 MiB (524 288 000 bytes).</summary>
    public long MaxUploadSizeBytes { get; set; } = 524_288_000;
}
