using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.Acquisition;

namespace MusicHub.Api.Controllers;

/// <summary>
/// The acquisition timeline of tracks: what was searched, found, downloaded, imported or failed, and when, as recorded
/// when it happened. Read only. Like the other endpoints it has no authentication of its own (internal-only).
/// </summary>
[ApiController]
[Route("api/tracks")]
public class TrackActivityController(TrackActivityLog log) : ControllerBase
{
    private const int DefaultPageSize = 50;
    private const int DefaultPerTrack = 5;

    /// <summary>
    /// One track's timeline, oldest event first, a page at a time (page size at most
    /// <see cref="TrackActivityLog.MaxPageSize"/>). 404 when the track does not exist; a track that was never searched
    /// has an empty timeline, not an invented one.
    /// </summary>
    [HttpGet("{id:int}/activity")]
    public async Task<IActionResult> ForTrack(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        await log.ForTrackAsync(id, page, pageSize, cancellationToken) is { } result ? Ok(result) : NotFound();

    /// <summary>
    /// The latest events of several tracks in one request, for a list: <paramref name="trackIds"/> is comma-separated (at
    /// most <see cref="TrackActivityLog.MaxTracksPerRequest"/>), <paramref name="perTrack"/> the number of events per track
    /// (at most <see cref="TrackActivityLog.MaxPerTrack"/>). The answer maps a track id to its events, oldest first; a
    /// track without events is absent.
    /// </summary>
    [HttpGet("activity")]
    public async Task<IActionResult> Latest(
        [FromQuery] string? trackIds,
        [FromQuery] int perTrack = DefaultPerTrack,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<int>();
        foreach (var part in (trackIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var id))
                return BadRequest(new ValidationProblemDetails(
                    new Dictionary<string, string[]> { ["trackIds"] = ["Track ids must be a comma-separated list of numbers."] }));
            ids.Add(id);
        }

        if (ids.Count > TrackActivityLog.MaxTracksPerRequest)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["trackIds"] = [$"At most {TrackActivityLog.MaxTracksPerRequest} tracks per request."],
            }));

        return Ok(await log.LatestForTracksAsync(ids, perTrack, cancellationToken));
    }
}
