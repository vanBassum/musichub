using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;

namespace MusicHub.Api.Controllers;

/// <summary>
/// The read-only identification history: every recorded outcome across job executions, newest first, independent of
/// Hangfire's retention. Like the Jobs endpoints it has no authentication of its own (internal-only).
/// </summary>
[ApiController]
[Route("api/identification-history")]
public class IdentificationHistoryController(MusicHubDbContext db) : ControllerBase
{
    private const int MaxPageSize = 100;

    /// <summary>The decisions that leave a file unlinked and need a person's eye; the default of <see cref="List"/>.</summary>
    private static readonly IdentificationDecision[] NeedsReview =
    [
        IdentificationDecision.NoMatch,
        IdentificationDecision.InsufficientEvidence,
        IdentificationDecision.NoMethodRan,
        IdentificationDecision.MethodFailed,
        IdentificationDecision.Error,
    ];

    /// <summary>
    /// A page of recorded outcomes. Without <paramref name="decision"/> only the unresolved ones; with it, that decision
    /// (Identified included, so linked files can be inspected too). Nothing here changes any identity.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] IdentificationDecision? decision,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var all = db.IdentificationOutcomes.AsNoTracking();
        var query = decision is { } chosen
            ? all.Where(o => o.Decision == chosen)
            : all.Where(o => NeedsReview.Contains(o.Decision));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);

        return Ok(new OutcomePage(rows.Select(JobsController.FileOutcome.From).ToList(), total, page, pageSize));
    }

    public sealed record OutcomePage(IReadOnlyList<JobsController.FileOutcome> Items, int Total, int Page, int PageSize);
}
