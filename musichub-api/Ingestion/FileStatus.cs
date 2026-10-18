namespace MusicHub.Api.Ingestion;

/// <summary>
/// Upload/storage lifecycle of a <see cref="FileRecord"/>. Only <see cref="Stored"/> means
/// storage confirmed the write. <see cref="Expired"/> is a stale unfinished upload that the cleanup
/// resolved as never stored. <see cref="Deleting"/> is a requested deletion whose storage cleanup is not confirmed yet;
/// <see cref="Deleted"/> is the tombstone of a deliberate deletion: the owned object is gone, the registration and
/// its catalog link remain. This is not metadata or import processing state.
/// </summary>
public enum FileStatus
{
    Pending,
    Uploading,
    Stored,
    Failed,
    Expired,
    Deleting,
    Deleted,
}
