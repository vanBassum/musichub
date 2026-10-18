namespace MusicHub.Api.Ingestion;

/// <summary>
/// Upload/storage lifecycle of a <see cref="FileRecord"/>. Only <see cref="Stored"/> means
/// storage confirmed the write. <see cref="Expired"/> is a stale unfinished upload that the cleanup
/// resolved as never stored. This is not metadata or import processing state.
/// </summary>
public enum FileStatus
{
    Pending,
    Uploading,
    Stored,
    Failed,
    Expired,
}
