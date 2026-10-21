using MusicHub.Api.Catalog;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Controllers;

/// <summary>
/// One persisted file registration in the <c>GET /api/files</c> listing. <c>Identification</c> is null unless the file
/// is Stored.
/// </summary>
public sealed record FileListItem(
    int Id,
    string OriginalFilename,
    long? SizeBytes,
    string Status,
    DateTimeOffset StartedAt,
    string? FailureReason,
    string? StatusDetail,
    string? DeletionError,
    FileIdentificationInfo? Identification)
{
    public static FileListItem From(FileListRow row) => new(
        row.File.Id, row.File.OriginalFilename, row.File.SizeBytes, row.File.Status.ToString(), row.File.StartedAt,
        row.File.FailureReason?.ToString(), row.File.StatusDetail, row.File.DeletionError, row.Identification);
}

/// <summary>A page of <see cref="FileListItem"/>s; <c>Total</c> is every registration matching the filters, not the page size.</summary>
public sealed record FileListResponse(IReadOnlyList<FileListItem> Items, int Total, int Page, int PageSize);

/// <summary>Every attempt (with what each method found) and every applied identity of one file, newest first.</summary>
public sealed record FileIdentificationResponse(
    IReadOnlyList<JobsController.FileOutcome> Attempts, IReadOnlyList<AppliedIdentificationItem> Applied);

public sealed record AppliedIdentificationItem(
    IdentificationSource Source, int? TrackId, string? TrackTitle, int? AttemptId, DateTimeOffset AppliedAt);
