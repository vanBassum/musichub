using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.Data;
using MusicHub.Api.Jobs;
using MusicHub.Api.Problems;

namespace MusicHub.Api.Controllers;

/// <summary>
/// A read-only overview of exceptional cases that identification and the background jobs already recorded, newest
/// first. It changes nothing and, like the Jobs endpoints, has no authentication of its own (internal-only).
/// </summary>
[ApiController]
[Route("api/problems")]
public class ProblemsController(CatalogReconciliationJobService jobs, MusicHubDbContext db) : ControllerBase
{
    /// <summary>
    /// The problems of every source that could be read. <c>unavailable</c> names the sources that couldn't (the job
    /// engine is not running), so an empty list is never mistaken for "no problems" there.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var problems = (await ProblemProjection.UnresolvedIdentificationsAsync(db, cancellationToken)).ToList();
        var unavailable = new List<string>();

        if (jobs.IsReady)
            problems.AddRange(ProblemProjection.FailedExecutions(await jobs.FailedAsync(ProblemProjection.MaxPerSource, cancellationToken)));
        else
            unavailable.Add(ProblemSources.Job);

        return Ok(new ProblemList(problems.OrderByDescending(p => p.OccurredAt).ToList(), unavailable));
    }

    public sealed record ProblemList(IReadOnlyList<Problem> Items, IReadOnlyList<string> Unavailable);
}
