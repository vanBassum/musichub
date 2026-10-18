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
        var limitResult = EnforceUploadLimit();
        if (limitResult is not null)
            return limitResult;

        var effectiveFilename = filename ?? "upload";
        FileRecord record;
        try
        {
            record = await ingestion.IngestAsync(
                Request.Body, effectiveFilename, Request.ContentType, cancellationToken);
        }
        catch (FileIngestionException ex)
        {
            // Never a 2xx: the file is not stored. The record (marked Failed when possible) is in GET /api/files.
            return Problem(
                title: "File upload failed",
                detail: "The file could not be stored. Check its status in GET /api/files and upload it again.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?> { ["fileId"] = ex.FileId });
        }

        return StatusCode(StatusCodes.Status201Created, ToResponse(record));
    }

    /// <summary>Lists the persisted file registrations, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var records = await ingestion.ListAsync(cancellationToken);
        return Ok(records.Select(r => new FileListItem(
            r.Id, r.OriginalFilename, r.SizeBytes, r.Status.ToString(), r.StartedAt)));
    }

    private static object ToResponse(FileRecord record) => new
    {
        record.Id,
        record.OriginalFilename,
        record.StorageLocation,
        record.StorageKey,
        Status = record.Status.ToString(),
        record.ContentHash,
        record.SizeBytes,
        record.Source,
        record.StartedAt,
    };

    private IActionResult? EnforceUploadLimit()
    {
        // The Kestrel feature is the only enforcement for chunked bodies where Content-Length is absent.
        var maxSizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxSizeFeature is { IsReadOnly: false })
            maxSizeFeature.MaxRequestBodySize = options.Value.MaxUploadSizeBytes;

        if (Request.ContentLength.HasValue && Request.ContentLength > options.Value.MaxUploadSizeBytes)
            return StatusCode(StatusCodes.Status413RequestEntityTooLarge);

        return null;
    }
}
