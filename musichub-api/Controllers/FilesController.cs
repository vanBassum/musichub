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
    /// <param name="size">Exact byte length of the body, computed by the client before sending.</param>
    /// <param name="sha256">Hex SHA-256 of the body, computed by the client before sending.</param>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(
        [FromQuery] string? filename,
        [FromQuery] long? size,
        [FromQuery] string? sha256,
        CancellationToken cancellationToken)
    {
        if (size is null or < 0 || sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit))
        {
            return Problem(
                title: "Missing upload checksum",
                detail: "Declare the file's byte length (size) and hex SHA-256 (sha256) before sending it.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var limitResult = EnforceUploadLimit(size.Value);
        if (limitResult is not null)
            return limitResult;

        var effectiveFilename = filename ?? "upload";
        FileRecord record;
        try
        {
            record = await ingestion.IngestAsync(
                Request.Body,
                effectiveFilename,
                Request.ContentType,
                new ExpectedUpload(size.Value, sha256.ToLowerInvariant()),
                cancellationToken);
        }
        catch (FileUploadRejectedException ex)
        {
            // Never a 2xx: the bytes that arrived are not the bytes the client declared.
            return Problem(
                title: "File upload rejected",
                detail: ex.Detail,
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["fileId"] = ex.FileId, ["reason"] = ex.Reason });
        }
        catch (FileIngestionException ex)
        {
            // Never a 2xx: the file is not stored. The record (marked Failed when possible) is in GET /api/files.
            return Problem(
                title: "File upload failed",
                detail: "The file could not be stored. Check its status in GET /api/files and upload it again.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?> { ["fileId"] = ex.FileId, ["reason"] = FileFailureReasons.StorageError });
        }

        return StatusCode(StatusCodes.Status201Created, ToResponse(record));
    }

    /// <summary>Lists the persisted file registrations, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var records = await ingestion.ListAsync(cancellationToken);
        return Ok(records.Select(r => new FileListItem(
            r.Id, r.OriginalFilename, r.SizeBytes, r.Status.ToString(), r.StartedAt, r.FailureReason, r.StatusDetail)));
    }

    /// <summary>Streams the original bytes of a Stored file as an attachment named after its original filename.</summary>
    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        (FileRecord Record, Stream Content)? opened;
        try
        {
            opened = await ingestion.OpenStoredAsync(id, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // The registration says Stored but the object is gone; say nothing about the storage key.
            return NotFound();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Problem(
                title: "File download failed",
                detail: "The file could not be read from storage.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (opened is null)
            return NotFound();

        // FileStreamResult disposes the stream and builds Content-Disposition: attachment with an
        // RFC 5987 filename*, so Unicode survives and CR/LF or quotes cannot break the header.
        return File(opened.Value.Content, "application/octet-stream", SafeDownloadName(opened.Value.Record.OriginalFilename));
    }

    /// <summary>The persisted name is untrusted: keep only its last path segment, without control characters.</summary>
    public static string SafeDownloadName(string originalFilename)
    {
        var lastSegment = originalFilename[(originalFilename.LastIndexOfAny(['/', '\\']) + 1)..];
        var cleaned = new string(lastSegment.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned is "" or "." or ".." ? "download" : cleaned;
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

    private IActionResult? EnforceUploadLimit(long declaredSize)
    {
        if (declaredSize > options.Value.MaxUploadSizeBytes)
            return StatusCode(StatusCodes.Status413RequestEntityTooLarge);

        // The Kestrel feature is the only enforcement for chunked bodies where Content-Length is absent.
        var maxSizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxSizeFeature is { IsReadOnly: false })
            maxSizeFeature.MaxRequestBodySize = options.Value.MaxUploadSizeBytes;

        if (Request.ContentLength.HasValue && Request.ContentLength > options.Value.MaxUploadSizeBytes)
            return StatusCode(StatusCodes.Status413RequestEntityTooLarge);

        return null;
    }
}
