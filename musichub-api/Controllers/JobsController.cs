using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;
using MusicHub.Api.Jobs;

namespace MusicHub.Api.Controllers;

/// <summary>
/// Lists the background tasks, shows their Hangfire executions and starts them by hand. Like the Files endpoints it
/// has no authentication of its own (owner decision on issue #109): it is internal-only and deployment/network
/// configuration must keep it from being publicly reachable. There is no Hangfire dashboard.
/// </summary>
[ApiController]
[Route("api/jobs")]
public class JobsController(JobService jobs, MusicHubDbContext db, IServiceProvider services) : ControllerBase
{
    private const int RecentExecutions = 25;

    /// <summary>The available background tasks; <c>activeExecutionId</c> is the task's run in progress, if any, and <c>schedule</c> its recurring schedule.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!jobs.IsReady)
            return JobsUnavailable();

        var tasks = new List<JobInfo>();
        foreach (var kind in JobKinds.All)
            tasks.Add(new JobInfo(
                kind.Id, kind.Name, kind.Description,
                await jobs.ActiveExecutionIdAsync(kind, cancellationToken),
                await jobs.ScheduleAsync(kind, cancellationToken)));
        return Ok(tasks.ToArray());
    }

    /// <summary>The most recent executions of all tasks, newest first.</summary>
    [HttpGet("executions")]
    public async Task<IActionResult> Executions(CancellationToken cancellationToken) =>
        jobs.IsReady
            ? Ok(await jobs.ListAsync(RecentExecutions, cancellationToken))
            : JobsUnavailable();

    /// <summary>One execution with its Hangfire state history. 404 when it is unknown or has expired.</summary>
    [HttpGet("executions/{id}")]
    public async Task<IActionResult> Execution(string id, CancellationToken cancellationToken)
    {
        if (!jobs.IsReady)
            return JobsUnavailable();

        return await jobs.GetAsync(id, cancellationToken) is { } details ? Ok(details) : NotFound();
    }

    /// <summary>
    /// What happened to each file (or downloaded candidate) the execution identified, in the order it went through
    /// them: one outcome per attempt with its decision, reason and what each identification method did. 404 when the
    /// execution is unknown or has expired.
    /// </summary>
    [HttpGet("executions/{id}/outcomes")]
    public async Task<IActionResult> Outcomes(string id, CancellationToken cancellationToken)
    {
        if (!jobs.IsReady)
            return JobsUnavailable();
        if (await jobs.GetAsync(id, cancellationToken) is null)
            return NotFound();

        var rows = await db.IdentificationOutcomes.AsNoTracking()
            .Where(o => o.JobExecutionId == id)
            .OrderBy(o => o.Id)
            .ToListAsync(cancellationToken);
        return Ok(rows.Select(FileOutcome.From));
    }

    /// <summary>
    /// The candidates a Search for Missing Music execution downloaded, in order, as they are recorded (so also while it
    /// runs). The download history is permanent: it stays when Hangfire drops the execution. 404 when the execution is
    /// unknown or has expired.
    /// </summary>
    [HttpGet("executions/{id}/downloads")]
    public async Task<IActionResult> Downloads(string id, CancellationToken cancellationToken)
    {
        if (!jobs.IsReady)
            return JobsUnavailable();
        if (await jobs.GetAsync(id, cancellationToken) is null)
            return NotFound();

        var rows = await db.AcquisitionDownloads.AsNoTracking()
            .Include(d => d.IntendedTrack)
            .Where(d => d.JobExecutionId == id)
            .OrderBy(d => d.Id)
            .ToListAsync(cancellationToken);
        return Ok(rows.Select(DownloadItem.From));
    }

    /// <summary>
    /// Enqueues a catalog reconciliation run: 202 with the new execution. 409 while a run is already active. With
    /// <paramref name="reIdentifyAll"/> (off by default) the run also identifies files that already have a track; it
    /// never changes their link.
    /// </summary>
    [HttpPost("catalog-reconciliation/start")]
    public Task<IActionResult> StartCatalogReconciliation(
        CancellationToken cancellationToken, [FromQuery] bool reIdentifyAll = false) =>
        StartAsync(JobKinds.CatalogReconciliation, cancellationToken, new JobStartOptions(reIdentifyAll));

    /// <summary>
    /// Enqueues a Search for Missing Music run: 202 with the new execution. 409 while a run is already active, and
    /// while no acquisition provider is configured.
    /// </summary>
    [HttpPost("missing-music-acquisition/start")]
    public Task<IActionResult> StartMissingMusicAcquisition(CancellationToken cancellationToken) =>
        MissingMusicAcquisition.AcquisitionProviderConfigured(services)
            ? StartAsync(JobKinds.MissingMusicAcquisition, cancellationToken)
            : Task.FromResult<IActionResult>(Problem(
                title: "No acquisition provider is configured",
                detail: MissingMusicAcquisition.NoProviderMessage,
                statusCode: StatusCodes.Status409Conflict));

    private async Task<IActionResult> StartAsync(JobKind kind, CancellationToken cancellationToken, JobStartOptions? options = null)
    {
        if (!jobs.IsReady)
            return JobsUnavailable();

        if (await jobs.TryStartAsync(kind, cancellationToken, options) is not { } id)
            return Problem(
                title: $"\"{kind.Name}\" is already running",
                detail: "A run is waiting or in progress. Wait for it to finish, then start it again.",
                statusCode: StatusCodes.Status409Conflict);

        return Accepted(new { executionId = id });
    }

    private ObjectResult JobsUnavailable() =>
        Problem(
            title: "Background jobs are not running",
            detail: "The job engine has not started yet or is disabled. Try again shortly.",
            statusCode: StatusCodes.Status503ServiceUnavailable);

    /// <summary>
    /// One recorded attempt. <c>JobExecutionId</c> is the execution that recorded it; that execution may have expired.
    /// <c>FileId</c> is set for a stored file, <c>DownloadId</c> for a downloaded candidate.
    /// </summary>
    public sealed record FileOutcome(
        string JobExecutionId, int? FileId, int? DownloadId, string FileName, IdentificationDecision Decision, string Reason,
        int? TrackId, DateTimeOffset RecordedAt, IReadOnlyList<IdentificationMethodResult> Methods, string? PipelineVersion)
    {
        public static FileOutcome From(IdentificationOutcome o) => new(
            o.JobExecutionId, o.FileId, o.DownloadId, o.FileName, o.Decision, o.Reason, o.TrackId, o.RecordedAt,
            o.ReadMethods(), o.PipelineVersion);
    }

    public sealed record DownloadItem(
        int Id, int? TrackId, string? TrackTitle, string Provider, string Source, string SourcePath, long SizeBytes,
        int? BitrateKbps, DownloadStatus Status, string Reason, string? FileSha256, string? AudioHash,
        AudioHashStatus? AudioHashStatus, int? FileId, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt)
    {
        public static DownloadItem From(AcquisitionDownload d) => new(
            d.Id, d.IntendedTrackId, d.IntendedTrack?.Title, d.Provider, d.Source, d.RemotePath, d.SizeBytes ?? 0,
            d.BitrateKbps, d.Status, d.Reason ?? "", d.ContentHash, d.AudioHash, d.AudioHashStatus, d.FileId,
            d.StartedAt, d.FinishedAt);
    }

    /// <summary><c>Schedule</c> is the recurring schedule with its next run; null for a task that cannot be scheduled.</summary>
    public sealed record JobInfo(string Id, string Name, string Description, string? ActiveExecutionId, JobScheduleInfo? Schedule);
}
