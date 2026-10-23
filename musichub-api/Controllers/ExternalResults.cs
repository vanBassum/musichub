using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.Catalog;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Controllers;

/// <summary>The answers every external-provider controller gives the same way.</summary>
public static class ExternalResults
{
    /// <summary>A missing, unreachable or rate-limited provider; distinct from an empty answer.</summary>
    public static ObjectResult Unavailable(this ControllerBase controller, string detail) =>
        controller.Problem(detail, statusCode: StatusCodes.Status503ServiceUnavailable, title: "External search is unavailable.");

    public const int MaxListLimit = 100;
    public const int DefaultListLimit = 25;

    /// <summary>Null when the limit of a provider list is within bounds.</summary>
    public static IActionResult? InvalidLimit(this ControllerBase controller, int limit) =>
        limit is >= 1 and <= MaxListLimit
            ? null
            : controller.BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["limit"] = [$"Limit must be between 1 and {MaxListLimit}."],
            }));

    public const int MaxOffset = 10_000;

    /// <summary>Null when the offset of a provider search page is within bounds.</summary>
    public static IActionResult? InvalidOffset(this ControllerBase controller, int offset) =>
        offset is >= 0 and <= MaxOffset
            ? null
            : controller.BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["offset"] = [$"Offset must be between 0 and {MaxOffset}."],
            }));

    /// <summary>Runs the answer against the provider; no provider or an unavailable one answers 503.</summary>
    public static async Task<IActionResult> WithProvider<T>(
        this ControllerBase controller, T? provider, Func<T, Task<IActionResult>> answer) where T : class
    {
        if (provider is null)
            return controller.Unavailable("External search is not configured.");
        try
        {
            return await answer(provider);
        }
        catch (CandidateSearchUnavailableException e)
        {
            return controller.Unavailable(e.Message);
        }
    }

    /// <summary>Null when the identifier is one of the provider's valid ones, written the way the provider writes it.</summary>
    public static IActionResult? InvalidId(this ControllerBase controller, string provider, string externalId, string what) =>
        ExternalSources.Normalize(provider, externalId) is { } normalized && normalized.ExternalId == externalId
            ? null
            : controller.BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["externalId"] = [$"Not a valid {provider} {what} identifier."],
            }));
}
