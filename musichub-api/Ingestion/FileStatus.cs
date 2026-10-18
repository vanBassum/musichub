namespace MusicHub.Api.Ingestion;

/// <summary>
/// Upload/storage lifecycle of a <see cref="FileRecord"/>. Only <see cref="Stored"/> means
/// storage confirmed the write. <see cref="Expired"/> is a stale unfinished upload that the cleanup
/// resolved as never stored. <see cref="Deleting"/> is a requested deletion whose storage cleanup is not confirmed yet;
/// a completed deletion removes the registration, so there is no deleted status. This is not metadata or import
/// processing state.
/// </summary>
public enum FileStatus
{
    Pending,
    Uploading,
    Stored,
    Failed,
    Expired,
    Deleting,
}
