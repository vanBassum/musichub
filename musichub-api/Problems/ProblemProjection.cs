using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Jobs;

namespace MusicHub.Api.Problems;

/// <summary>
/// One exceptional case that something already recorded. <paramref name="Source"/> says which record it comes from
/// (<see cref="ProblemSources"/>) and <paramref name="Kind"/> is that source's own state or decision name; there is no
/// severity, recovery or resolution state because neither source records one. <paramref name="JobExecutionId"/> is the
/// Jobs execution that holds the details, when there is one.
/// </summary>
public sealed record Problem(
    string Id,
    string Source,
    string Kind,
    string Subject,
    string Reason,
    DateTimeOffset OccurredAt,
    string? JobExecutionId,
    int? FileId);

public static class ProblemSources
{
    public const string Identification = "identification";
    public const string Job = "job";
}

/// <summary>
/// Reads, never writes: it projects the identification outcomes and the failed Hangfire executions into
/// <see cref="Problem"/>s. Those two stay the authoritative records. A new kind of problem is one more method here.
/// </summary>
public static class ProblemProjection
{
    public const int MaxPerSource = 100;

    // The decisions that leave a file unlinked without an identity: Identified linked the file, Skipped never touched it.
    private static readonly IdentificationDecision[] Unresolved =
    [
        IdentificationDecision.NoMatch,
        IdentificationDecision.InsufficientEvidence,
        IdentificationDecision.NoMethodRan,
        IdentificationDecision.MethodFailed,
        IdentificationDecision.Error,
    ];

    /// <summary>
    /// Stored files that still have no track, whose latest recorded identification attempt ended unresolved. A file
    /// that was linked since (or deleted) is no longer a problem, and an older attempt never shows twice. Outcomes
    /// live as long as Hangfire keeps their execution, so a problem disappears with them.
    /// </summary>
    public static async Task<IReadOnlyList<Problem>> UnresolvedIdentificationsAsync(
        MusicHubDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.IdentificationOutcomes.AsNoTracking()
            .Where(o => !db.IdentificationOutcomes.Any(newer => newer.FileId == o.FileId && newer.Id > o.Id))
            .Where(o => Unresolved.Contains(o.Decision))
            .Where(o => o.File!.Status == FileStatus.Stored && o.File.TrackId == null)
            .OrderByDescending(o => o.Id)
            .Take(MaxPerSource)
            .ToListAsync(cancellationToken);

        return rows.Select(o => new Problem(
            $"{ProblemSources.Identification}:{o.Id}",
            ProblemSources.Identification,
            o.Decision.ToString(),
            o.FileName,
            o.Reason,
            o.RecordedAt,
            o.JobExecutionId,
            o.FileId)).ToList();
    }

    /// <summary>
    /// Executions that ended in Hangfire's Failed state. The reason names the exception type only, never its message:
    /// that text can hold paths or connection details. The Jobs execution has the rest.
    /// </summary>
    public static IReadOnlyList<Problem> FailedExecutions(IEnumerable<JobExecutionDetails> executions) =>
        executions
            .Where(d => d.Execution.State == "Failed")
            .Select(d => new Problem(
                $"{ProblemSources.Job}:{d.Execution.Id}",
                ProblemSources.Job,
                d.Execution.State,
                "Catalog synchronization",
                d.ErrorType is { } type
                    ? $"The run stopped after an error ({type[(type.LastIndexOf('.') + 1)..]})."
                    : "The run stopped after an error.",
                new DateTimeOffset(DateTime.SpecifyKind(d.Execution.FinishedAt ?? d.Execution.CreatedAt, DateTimeKind.Utc)),
                d.Execution.Id,
                null))
            .ToList();
}
