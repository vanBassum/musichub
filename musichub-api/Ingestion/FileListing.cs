using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Ingestion;

/// <summary>How a Stored file stands in identification; see <see cref="FileIdentificationFilter"/> for the states.</summary>
/// <param name="Source">Who applied the file's current link; null while it is not linked.</param>
/// <param name="Decision">How the latest recorded attempt ended; null when none is recorded.</param>
public sealed record FileIdentificationInfo(
    FileIdentificationFilter State,
    IdentificationSource? Source,
    int? TrackId,
    string? TrackTitle,
    IdentificationDecision? Decision,
    string? Reason,
    DateTimeOffset? RecordedAt);

/// <summary>One listed registration; <see cref="Identification"/> is null unless the file is Stored.</summary>
public sealed record FileListRow(FileRecord File, FileIdentificationInfo? Identification);

public sealed record FileListPage(IReadOnlyList<FileListRow> Items, int Total, int Page, int PageSize);

/// <summary>Registrations and Stored files per identification state, whatever filter a list is showing.</summary>
public sealed record FileCounts(
    int Total,
    IReadOnlyDictionary<FileStatus, int> Statuses,
    IReadOnlyDictionary<FileIdentificationFilter, int> Identification);

/// <summary>Every recorded attempt and every applied identity of one file, newest first. Read-only; nothing here is deleted.</summary>
public sealed record FileIdentificationHistory(
    IReadOnlyList<IdentificationOutcome> Attempts, IReadOnlyList<AppliedIdentification> Applied);

/// <summary>The read side of the file registrations: filtered, paged lists with their total, counts and identification history.</summary>
public sealed class FileListing(MusicHubDbContext db)
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    /// <summary>
    /// One page of the registrations that match <paramref name="status"/> and <paramref name="identification"/> (both
    /// optional, combined with AND), newest first. <c>Total</c> counts every match, not just the page.
    /// </summary>
    public async Task<FileListPage> ListAsync(
        FileStatus? status, FileIdentificationFilter? identification, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Files.AsNoTracking();
        if (status is { } chosenStatus)
            query = query.Where(f => f.Status == chosenStatus);
        if (identification is { } chosenState)
            query = query.Matching(db, chosenState);

        var total = await query.CountAsync(cancellationToken);
        var files = await query
            .Include(f => f.Track)
            .OrderByDescending(f => f.StartedAt)
            .ThenByDescending(f => f.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        var info = await IdentificationOfAsync(files.Where(f => f.Status == FileStatus.Stored).ToList(), cancellationToken);
        return new FileListPage(
            files.Select(f => new FileListRow(f, info.GetValueOrDefault(f.Id))).ToList(), total, page, pageSize);
    }

    public async Task<FileCounts> CountAsync(CancellationToken cancellationToken)
    {
        var statuses = await db.Files.AsNoTracking()
            .GroupBy(f => f.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var identification = new Dictionary<FileIdentificationFilter, int>();
        foreach (var state in Enum.GetValues<FileIdentificationFilter>())
            identification[state] = await db.Files.AsNoTracking().Matching(db, state).CountAsync(cancellationToken);
        return new FileCounts(
            statuses.Sum(s => s.Count), statuses.ToDictionary(s => s.Status, s => s.Count), identification);
    }

    /// <summary>The attempts (with the evidence each method gave) and applied identities of file <paramref name="id"/>; null when there is no such file.</summary>
    public async Task<FileIdentificationHistory?> HistoryAsync(int id, CancellationToken cancellationToken)
    {
        if (!await db.Files.AnyAsync(f => f.Id == id, cancellationToken))
            return null;
        var attempts = await db.IdentificationOutcomes.AsNoTracking()
            .Where(o => o.FileId == id).OrderByDescending(o => o.Id).ToListAsync(cancellationToken);
        var applied = await db.AppliedIdentifications.AsNoTracking()
            .Include(a => a.Track)
            .Where(a => a.FileId == id).OrderByDescending(a => a.Id).ToListAsync(cancellationToken);
        return new FileIdentificationHistory(attempts, applied);
    }

    private async Task<Dictionary<int, FileIdentificationInfo>> IdentificationOfAsync(
        IReadOnlyList<FileRecord> stored, CancellationToken cancellationToken)
    {
        var ids = stored.Select(f => f.Id).ToList();
        // Only the columns the list shows: the method evidence is loaded by HistoryAsync, on demand.
        var attempts = (await db.IdentificationOutcomes.AsNoTracking()
                .Where(o => o.FileId != null && ids.Contains(o.FileId.Value))
                .Select(o => new { o.Id, FileId = o.FileId!.Value, o.Decision, o.Reason, o.RecordedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(o => o.FileId)
            .ToDictionary(g => g.Key, g => g.MaxBy(o => o.Id)!);
        var sources = (await db.AppliedIdentifications.AsNoTracking()
                .Where(a => ids.Contains(a.FileId))
                .Select(a => new { a.Id, a.FileId, a.Source })
                .ToListAsync(cancellationToken))
            .GroupBy(a => a.FileId)
            .ToDictionary(g => g.Key, g => g.MaxBy(a => a.Id)!.Source);

        return stored.ToDictionary(f => f.Id, f =>
        {
            var latest = attempts.GetValueOrDefault(f.Id);
            var linked = f.TrackId is not null;
            var source = linked ? sources.GetValueOrDefault(f.Id, IdentificationSource.Automatic) : (IdentificationSource?)null;
            return new FileIdentificationInfo(
                FileIdentification.StateOf(linked, source == IdentificationSource.Manual, latest?.Decision),
                source, f.TrackId, f.Track?.Title, latest?.Decision, latest?.Reason, latest?.RecordedAt);
        });
    }
}
