using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.Acquisition;

namespace MusicHub.Api.Controllers;

/// <summary>
/// The download queue: what is queued, downloading or done, with status, progress and a safe error, and the two manual
/// actions, retry and remove. It names the provider and the file, never the source or the remote path. Like the Jobs
/// endpoints it has no authentication of its own (internal-only).
/// </summary>
[ApiController]
[Route("api/downloads")]
public class DownloadsController(IDownloadQueueService queue) : ControllerBase
{
    private const int MaxPageSize = 100;

    /// <summary>A page of requests, newest first; <paramref name="status"/> narrows it to one status.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DownloadRequestStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        return Ok(await queue.ListAsync(status, page, pageSize, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        await queue.GetAsync(id, cancellationToken) is { } request ? Ok(request) : NotFound();

    /// <summary>
    /// Queues a Failed request again (200 with the request as it is now). 409 when it is not Failed (any more) or the track
    /// or candidate already has an active request: nothing changed, so repeating a click never starts a second transfer.
    /// </summary>
    [HttpPost("{id:int}/retry")]
    public async Task<IActionResult> Retry(int id, CancellationToken cancellationToken)
    {
        var result = await queue.RetryAsync(id, cancellationToken);
        return result.Outcome switch
        {
            RetryOutcome.Retried => Ok(result.Request),
            RetryOutcome.NotFound => NotFound(),
            RetryOutcome.NotFailed => Problem(
                title: "Download is not failed",
                detail: "Only a failed download can be retried, and this one is not failed (any more). Refresh the list.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Problem(
                title: "Download already queued",
                detail: "This track or this file already has an active download, so it was not queued again.",
                statusCode: StatusCodes.Status409Conflict),
        };
    }

    /// <summary>
    /// Removes a Failed or Completed request from the visible queue (204); the wishlist, the catalog and the stored files
    /// are untouched. 404 when it is not in the queue (already removed). 409 for an active request: a transfer is not
    /// cancelled, so wait until it ends.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Remove(int id, CancellationToken cancellationToken) =>
        await queue.RemoveAsync(id, cancellationToken) switch
        {
            RemoveOutcome.Removed => NoContent(),
            RemoveOutcome.NotFound => NotFound(),
            _ => Problem(
                title: "Download cannot be removed",
                detail: "Only a failed or completed download can be removed. An active download cannot be cancelled; wait until it ends.",
                statusCode: StatusCodes.Status409Conflict),
        };
}
