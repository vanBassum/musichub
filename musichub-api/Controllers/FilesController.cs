using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MusicHub.Api.Catalog;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Controllers;

/// <summary>
/// Accepts file uploads and hands them to <see cref="FileIngestionService"/>.
/// This endpoint is internal-only; deployment/network configuration must enforce that
/// it is not publicly reachable (per the accepted internal-file-upload-contract decision).
/// </summary>
[ApiController]
[Route("api/files")]
public class FilesController(
    FileIngestionService ingestion, FileDeletionService deletion, FileListing listing,
    IOptions<FileIngestionOptions> options) : ControllerBase
{
    /// <summary>
    /// Streams the raw request body into managed storage and returns the registered file record.
    /// </summary>
    /// <param name="filename">Original filename. Falls back to "upload" when not supplied.</param>
    /// <param name="expectedLength">Declared byte length of the body (required).</param>
    /// <param name="expectedSha256">Declared SHA-256 (hex) of the body (required). The upload is Stored only if length and digest both match.</param>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(
        [FromQuery] string? filename,
        [FromQuery] long? expectedLength,
        [FromQuery] string? expectedSha256,
        CancellationToken cancellationToken)
    {
        var expected = expectedLength is { } length && expectedSha256 is not null
            ? UploadExpectation.TryCreate(length, expectedSha256)
            : null;
        if (expected is null)
            return Problem(
                title: "Invalid upload verification values",
                detail: "expectedLength (a non-negative byte count) and expectedSha256 (64 hex characters) are required.",
                statusCode: StatusCodes.Status400BadRequest);

        var limitResult = EnforceUploadLimit();
        if (limitResult is not null)
            return limitResult;

        var effectiveFilename = filename ?? "upload";
        FileRecord record;
        try
        {
            record = await ingestion.IngestAsync(
                Request.Body, effectiveFilename, Request.ContentType, expected, cancellationToken);
        }
        catch (UploadIntegrityException ex)
        {
            // The bytes that arrived are not the bytes the client declared; the record is Failed.
            return Problem(
                title: "Upload verification failed",
                detail: $"{ex.Detail} The file was not stored; upload it again.",
                statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["fileId"] = ex.FileId, ["reason"] = ex.Failure.ToString() });
        }
        catch (FileIngestionException ex)
        {
            // Never a 2xx: the file is not stored. The record (marked Failed when possible) is in GET /api/files.
            return Problem(
                title: "File upload failed",
                detail: $"{ex.Detail} Check its status in GET /api/files and upload it again.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?> { ["fileId"] = ex.FileId, ["reason"] = UploadIntegrityFailure.StorageError.ToString() });
        }

        return StatusCode(StatusCodes.Status201Created, ToResponse(record));
    }

    /// <summary>
    /// A page of the persisted file registrations, newest first. <paramref name="status"/> and
    /// <paramref name="identification"/> narrow it (both optional); <c>total</c> counts every match, not the page.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] FileStatus? status,
        [FromQuery] FileIdentificationFilter? identification,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = FileListing.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await listing.ListAsync(status, identification, page, pageSize, cancellationToken);
        return Ok(new FileListResponse(result.Items.Select(FileListItem.From).ToList(), result.Total, result.Page, result.PageSize));
    }

    /// <summary>How many registrations have each status and how many Stored files are in each identification state, whatever filter a list shows.</summary>
    [HttpGet("counts")]
    public async Task<IActionResult> Counts(CancellationToken cancellationToken) =>
        Ok(await listing.CountAsync(cancellationToken));

    /// <summary>
    /// Everything recorded about identifying one file: each attempt with its reason and what every method found, and each
    /// identity applied to it (automatic or manual). Read-only; 404 when there is no such file.
    /// </summary>
    [HttpGet("{id:int}/identification")]
    public async Task<IActionResult> Identification(int id, CancellationToken cancellationToken)
    {
        var history = await listing.HistoryAsync(id, cancellationToken);
        if (history is null)
            return NotFound();
        return Ok(new FileIdentificationResponse(
            history.Attempts.Select(JobsController.FileOutcome.From).ToList(),
            history.Applied.Select(a => new AppliedIdentificationItem(
                a.Source, a.TrackId, a.Track?.Title, a.AttemptId, a.AppliedAt)).ToList()));
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

    /// <summary>
    /// Streams a Stored file inline as audio for browser playback, with byte-range support (206) so seeking works.
    /// 404 when the file is absent, not Stored or its object is gone; 415 when its extension isn't a known audio type;
    /// 416 for an unsatisfiable range; 502 when storage cannot be read. Read-only; the download endpoint is unchanged.
    /// </summary>
    [HttpGet("{id:int}/audio")]
    public async Task<IActionResult> Audio(int id, CancellationToken cancellationToken)
    {
        var record = await ingestion.FindStoredAsync(id, cancellationToken);
        if (record is null)
            return NotFound();

        var contentType = AudioStreaming.ContentTypeFor(record.OriginalFilename);
        if (contentType is null)
            return Problem(
                title: "Not a playable audio file",
                detail: "The file's extension is not a supported audio type.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);

        if (record.SizeBytes is not { } total)
            return Problem(
                title: "File length unknown",
                detail: "The file cannot be streamed without its stored length.",
                statusCode: StatusCodes.Status502BadGateway);

        Response.Headers.AcceptRanges = "bytes";
        Response.Headers.XContentTypeOptions = "nosniff";

        var range = AudioStreaming.ResolveRange(Request.Headers.Range.ToString(), total);
        if (!range.IsSatisfiable)
        {
            Response.Headers.ContentRange = $"bytes */{total}";
            return StatusCode(StatusCodes.Status416RangeNotSatisfiable);
        }

        if (total == 0)
            return File(Stream.Null, contentType);

        Stream content;
        try
        {
            content = await ingestion.OpenStoredRangeAsync(record, range.Start, range.End, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Problem(
                title: "File playback failed",
                detail: "The file could not be read from storage.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        Response.ContentLength = range.Length;
        if (range.IsPartial)
        {
            Response.StatusCode = StatusCodes.Status206PartialContent;
            Response.Headers.ContentRange = $"bytes {range.Start}-{range.End}/{total}";
        }
        return File(content, contentType);
    }

    /// <summary>
    /// Deletes the file whatever its status: removes its object and its registration (204), so it leaves the list.
    /// Retrying a Deleting file is safe; a file that is already gone is 404. 409 while an upload for it is running, 502 when storage could not be cleaned.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await deletion.DeleteAsync(id, cancellationToken) switch
            {
                FileDeletionOutcome.Deleted => NoContent(),
                FileDeletionOutcome.NotFound => NotFound(),
                _ => Problem(
                    title: "File is being uploaded",
                    detail: "An upload for this file is still running. Wait for it to finish, then delete it.",
                    statusCode: StatusCodes.Status409Conflict),
            };
        }
        catch (FileDeletionException ex)
        {
            return Problem(
                title: "File deletion failed",
                detail: $"{ex.Detail} The file stays in the list as Deleting: delete it again to retry.",
                statusCode: StatusCodes.Status502BadGateway,
                extensions: new Dictionary<string, object?> { ["fileId"] = ex.FileId });
        }
    }

    /// <summary>
    /// Manually deletes Failed files after UI confirmation, as approved in musichub-understanding PR #27.
    /// Deletes every file that is Failed at the moment of the request (object and registration, like <see cref="Delete"/>).
    /// Always 200 with the counts: files that were no longer Failed or are being uploaded are skipped, and files whose
    /// storage could not be cleaned are listed under <c>failed</c> and stay in the list as Deleting.
    /// </summary>
    [HttpPost("delete-failed")]
    public async Task<IActionResult> DeleteFailed(CancellationToken cancellationToken) =>
        Ok(await deletion.DeleteFailedAsync(cancellationToken));

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
