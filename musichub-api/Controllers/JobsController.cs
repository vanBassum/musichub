using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.Jobs;

namespace MusicHub.Api.Controllers;

/// <summary>
/// Lists the background tasks, shows their Hangfire executions and starts catalog reconciliation by hand. Like the
/// Files endpoints it has no authentication of its own (owner decision on issue #109): it is internal-only and
/// deployment/network configuration must keep it from being publicly reachable. There is no Hangfire dashboard.
/// </summary>
[ApiController]
[Route("api/jobs")]
public class JobsController(CatalogReconciliationJobService catalogReconciliation) : ControllerBase
{
    private const string CatalogReconciliationId = "catalog-reconciliation";
    private const int RecentExecutions = 25;

    /// <summary>The available background tasks. Only catalog reconciliation exists; <c>activeExecutionId</c> is its run in progress, if any.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!catalogReconciliation.IsReady)
            return JobsUnavailable();

        return Ok(new[]
        {
            new JobInfo(
                CatalogReconciliationId,
                "Synchronize catalog",
                "Looks for stored files that are not linked to the catalog and tries to link them.",
                await catalogReconciliation.ActiveExecutionIdAsync(cancellationToken)),
        });
    }

    /// <summary>The most recent executions of all tasks, newest first.</summary>
    [HttpGet("executions")]
    public async Task<IActionResult> Executions(CancellationToken cancellationToken) =>
        catalogReconciliation.IsReady
            ? Ok(await catalogReconciliation.ListAsync(RecentExecutions, cancellationToken))
            : JobsUnavailable();

    /// <summary>One execution with its Hangfire state history. 404 when it is unknown or has expired.</summary>
    [HttpGet("executions/{id}")]
    public async Task<IActionResult> Execution(string id, CancellationToken cancellationToken)
    {
        if (!catalogReconciliation.IsReady)
            return JobsUnavailable();

        return await catalogReconciliation.GetAsync(id, cancellationToken) is { } details ? Ok(details) : NotFound();
    }

    /// <summary>Enqueues a catalog reconciliation run: 202 with the new execution. 409 while a run is already active.</summary>
    [HttpPost("catalog-reconciliation/start")]
    public async Task<IActionResult> StartCatalogReconciliation(CancellationToken cancellationToken)
    {
        if (!catalogReconciliation.IsReady)
            return JobsUnavailable();

        if (await catalogReconciliation.TryStartAsync(cancellationToken) is not { } id)
            return Problem(
                title: "Catalog synchronization is already running",
                detail: "A run is waiting or in progress. Wait for it to finish, then start it again.",
                statusCode: StatusCodes.Status409Conflict);

        return Accepted(new { executionId = id });
    }

    private ObjectResult JobsUnavailable() =>
        Problem(
            title: "Background jobs are not running",
            detail: "The job engine has not started yet or is disabled. Try again shortly.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    public sealed record JobInfo(string Id, string Name, string Description, string? ActiveExecutionId);
}
