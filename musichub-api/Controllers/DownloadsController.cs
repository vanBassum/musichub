using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.Acquisition;

namespace MusicHub.Api.Controllers;

/// <summary>
/// The read-only download queue: what is queued, downloading or done, with status, progress and a safe error. It
/// names the provider and the file, never the source or the remote path. Like the Jobs endpoints it has no
/// authentication of its own (internal-only).
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
}
