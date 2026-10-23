using Microsoft.AspNetCore.Mvc;
using MusicHub.Api.Catalog;

namespace MusicHub.Api.Controllers;

/// <summary>The answers every external-provider controller gives the same way.</summary>
public static class ExternalResults
{
    /// <summary>A missing, unreachable or rate-limited provider; distinct from an empty answer.</summary>
    public static ObjectResult Unavailable(this ControllerBase controller, string detail) =>
        controller.Problem(detail, statusCode: StatusCodes.Status503ServiceUnavailable, title: "External search is unavailable.");

    /// <summary>Null when the identifier is one of the provider's valid ones, written the way the provider writes it.</summary>
    public static IActionResult? InvalidId(this ControllerBase controller, string provider, string externalId, string what) =>
        ExternalSources.Normalize(provider, externalId) is { } normalized && normalized.ExternalId == externalId
            ? null
            : controller.BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["externalId"] = [$"Not a valid {provider} {what} identifier."],
            }));
}
