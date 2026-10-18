using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Controllers;

/// <summary>
/// Accepts file uploads and hands them to <see cref="FileIngestionService"/>.
/// This endpoint is internal-only; deployment/network configuration must enforce that
/// it is not publicly reachable (per the accepted internal-file-upload-contract decision).
/// </summary>
[ApiController]
[Route("api/files")]
public class FilesController(FileIngestionService ingestion, IOptions<FileIngestionOptions> options) : ControllerBase
{
    /// <summary>
    /// Streams the raw request body into managed storage and returns the registered file record.
    /// </summary>
    /// <param name="filename">Original filename. Falls back to "upload" when not supplied.</param>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(
        [FromQuery] string? filename,
        CancellationToken cancellationToken)
    {
        // Set a per-request body size limit via the Kestrel feature (no-op on other hosts).
        var maxSizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxSizeFeature is { IsReadOnly: false })
            maxSizeFeature.MaxRequestBodySize = options.Value.MaxUploadSizeBytes;

        // Early rejection when Content-Length is provided and exceeds the limit.
        if (Request.ContentLength.HasValue && Request.ContentLength > options.Value.MaxUploadSizeBytes)
            return StatusCode(StatusCodes.Status413RequestEntityTooLarge);

        var effectiveFilename = filename ?? "upload";
        var record = await ingestion.IngestAsync(
            Request.Body, effectiveFilename, Request.ContentType, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, new
        {
            record.Id,
            record.OriginalFilename,
            record.StorageLocation,
            record.StorageKey,
            record.ContentHash,
            record.Source,
            record.StartedAt,
        });
    }
}
