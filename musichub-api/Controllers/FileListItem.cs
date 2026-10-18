namespace MusicHub.Api.Controllers;

/// <summary>One persisted file registration in the <c>GET /api/files</c> listing.</summary>
public sealed record FileListItem(
    int Id,
    string OriginalFilename,
    long? SizeBytes,
    string Status,
    DateTimeOffset StartedAt,
    string? FailureReason,
    string? StatusDetail,
    string? DeletionError);
