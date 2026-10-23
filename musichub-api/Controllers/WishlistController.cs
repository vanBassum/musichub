using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.Acquisition;

namespace MusicHub.Api.Controllers;

/// <summary>
/// The wishlist overview of wanted tracks and where their acquisition stands, and Search now for one track. Like the
/// other endpoints it has no authentication of its own (internal-only).
/// </summary>
[ApiController]
[Route("api/wishlist")]
public class WishlistController(WishlistService wishlist) : ControllerBase
{
    /// <summary>A page of the overview, most recent activity first; <paramref name="state"/> narrows it. The counts cover every state.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] WishlistState? state,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await wishlist.ListAsync(state, page, pageSize, cancellationToken));

    /// <summary>
    /// Schedules a search for the track through the Search for Missing Music run: 202 with the execution. 404 when the
    /// track does not exist; 409 when it has a file, is being searched or downloaded (so a repeated click schedules
    /// nothing twice) or no provider is configured; 503 while the job engine is not running.
    /// </summary>
    [HttpPost("{trackId:int}/search")]
    public async Task<IActionResult> SearchNow(int trackId, CancellationToken cancellationToken)
    {
        var result = await wishlist.SearchNowAsync(trackId, cancellationToken);
        return result.Outcome switch
        {
            SearchNowOutcome.Scheduled => Accepted(new { executionId = result.ExecutionId }),
            SearchNowOutcome.TrackNotFound => NotFound(),
            SearchNowOutcome.NoProvider => Problem(
                title: "No acquisition provider is configured",
                detail: MissingMusicAcquisition.NoProviderMessage,
                statusCode: StatusCodes.Status409Conflict),
            SearchNowOutcome.NotEligible => Problem(
                title: "The track cannot be searched now",
                detail: "It has a stored file, or a search or download for it is already under way. Refresh the list.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Problem(
                title: "Background jobs are not running",
                detail: "The job engine has not started yet or is disabled. Try again shortly.",
                statusCode: StatusCodes.Status503ServiceUnavailable),
        };
    }
}
