namespace MusicHub.Api.Ingestion;

/// <summary>Machine-readable <see cref="FileRecord.FailureReason"/> values; never exception text.</summary>
public static class FileFailureReasons
{
    public const string Incomplete = "Incomplete";
    public const string Corrupt = "Corrupt";
    public const string StorageError = "StorageError";
}
