namespace MusicHub.Api.Ingestion;

/// <summary>
/// Upload/storage lifecycle of a <see cref="FileRecord"/>. Only <see cref="Stored"/> means
/// storage confirmed the write. This is not metadata or import processing state.
/// </summary>
public enum FileStatus
{
    Pending,
    Uploading,
    Stored,
    Failed,
}
