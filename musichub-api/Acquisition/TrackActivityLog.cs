using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;

namespace MusicHub.Api.Acquisition;

/// <summary>An event as the API shows it: no more than the sanitized sentences, the counts and the correlation ids.</summary>
public sealed record TrackActivityView(
    long Id,
    int TrackId,
    TrackActivityKind Kind,
    DateTimeOffset OccurredAt,
    string Summary,
    string? Detail,
    Guid? SearchId,
    int? DownloadRequestId,
    int? AcquisitionDownloadId,
    string? JobExecutionId,
    int? AttemptNumber,
    int? ResultCount,
    int? PlausibleCount,
    int? EligibleCount);

/// <summary>The optional correlation ids and counts an event can carry; set only the ones that apply.</summary>
public sealed record TrackActivityLinks
{
    public Guid? SearchId { get; init; }
    public int? DownloadRequestId { get; init; }
    public int? DownloadId { get; init; }
    public string? JobExecutionId { get; init; }
    public int? AttemptNumber { get; init; }
    public int? ResultCount { get; init; }
    public int? PlausibleCount { get; init; }
    public int? EligibleCount { get; init; }
}

/// <summary>One page of a track's timeline, oldest event first.</summary>
public sealed record TrackActivityPage(IReadOnlyList<TrackActivityView> Items, int Total, int Page, int PageSize);

/// <summary>
/// The one owner of the per-track acquisition timeline: it stores the events the search, the queue and the download
/// history report, and reads them back in the order they were written (each event is written when it happens, so the id
/// is the chronology; <see cref="TrackActivity.OccurredAt"/> is the time shown). It decides nothing about acquisition and never invents an event: a
/// stage that did not happen has no row, and data from before this log existed is not backfilled. Scoped, like the
/// context it uses. The text it stores goes through <see cref="Sanitize"/>.
/// </summary>
public class TrackActivityLog(MusicHubDbContext db, TimeProvider time)
{
    public const int MaxPageSize = 200;
    public const int MaxTracksPerRequest = 200;
    public const int MaxPerTrack = 20;

    /// <summary>
    /// Adds an event to the context without saving, so the caller stores it in the same save as the transition it
    /// reports: the event exists exactly when the transition happened.
    /// </summary>
    public TrackActivity Stage(
        int trackId, TrackActivityKind kind, string summary, string? detail = null, TrackActivityLinks? links = null)
    {
        links ??= new TrackActivityLinks();
        var activity = new TrackActivity
        {
            TrackId = trackId,
            Kind = kind,
            OccurredAt = time.GetUtcNow(),
            Summary = Sanitize(summary, TrackActivity.SummaryMaxLength),
            Detail = detail is null ? null : Sanitize(detail, TrackActivity.DetailMaxLength),
            SearchId = links.SearchId,
            DownloadRequestId = links.DownloadRequestId,
            AcquisitionDownloadId = links.DownloadId,
            JobExecutionId = links.JobExecutionId is null
                ? null : AcquisitionHistory.Truncate(links.JobExecutionId, AcquisitionDownload.JobExecutionIdMaxLength),
            AttemptNumber = links.AttemptNumber,
            ResultCount = links.ResultCount,
            PlausibleCount = links.PlausibleCount,
            EligibleCount = links.EligibleCount,
        };
        db.TrackActivities.Add(activity);
        return activity;
    }

    /// <summary>Stores one event on its own.</summary>
    public async Task RecordAsync(
        int trackId, TrackActivityKind kind, string summary, CancellationToken ct, string? detail = null,
        TrackActivityLinks? links = null)
    {
        Stage(trackId, kind, summary, detail, links);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A page of the track's timeline, oldest first; null when the track does not exist.</summary>
    public async Task<TrackActivityPage?> ForTrackAsync(int trackId, int page, int pageSize, CancellationToken ct)
    {
        if (!await db.Tracks.AnyAsync(t => t.Id == trackId, ct))
            return null;

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = db.TrackActivities.AsNoTracking().Where(a => a.TrackId == trackId);
        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
        return new TrackActivityPage(rows.Select(ToView).ToList(), total, page, pageSize);
    }

    /// <summary>
    /// The latest events of many tracks in one query (a list shows each track's recent activity without one request per
    /// track). Each track's list is oldest first; a track without events is absent.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<TrackActivityView>>> LatestForTracksAsync(
        IReadOnlyCollection<int> trackIds, int perTrack, CancellationToken ct)
    {
        var ids = trackIds.Distinct().Take(MaxTracksPerRequest).ToList();
        perTrack = Math.Clamp(perTrack, 1, MaxPerTrack);
        if (ids.Count == 0)
            return new Dictionary<int, IReadOnlyList<TrackActivityView>>();

        // An event is among its track's latest when fewer than perTrack later events exist for that track.
        var rows = await db.TrackActivities.AsNoTracking()
            .Where(a => ids.Contains(a.TrackId)
                && db.TrackActivities.Count(later => later.TrackId == a.TrackId && later.Id > a.Id) < perTrack)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        return rows
            .GroupBy(a => a.TrackId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TrackActivityView>)g.Select(ToView).ToList());
    }

    private static TrackActivityView ToView(TrackActivity a) => new(
        a.Id, a.TrackId, a.Kind, a.OccurredAt, a.Summary, a.Detail, a.SearchId, a.DownloadRequestId,
        a.AcquisitionDownloadId, a.JobExecutionId, a.AttemptNumber, a.ResultCount, a.PlausibleCount, a.EligibleCount);

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(200);

    // Order matters: addresses and credentials go before the path rules, which would otherwise cut into them.
    private static readonly (Regex Pattern, string Replacement)[] Redactions =
    [
        (new Regex(@"\b[a-z][a-z0-9+.\-]*://\S+", RegexOptions.IgnoreCase, MatchTimeout), "[address]"),
        (new Regex(@"\b(bearer|basic)\s+[A-Za-z0-9._~+/=\-]+", RegexOptions.IgnoreCase, MatchTimeout), "$1 [redacted]"),
        (new Regex(@"\b(api[_\- ]?key|token|password|passwd|secret|authorization|credentials?)\b(\s*[:=]\s*)\S+",
            RegexOptions.IgnoreCase, MatchTimeout), "$1$2[redacted]"),
        (new Regex(@"\\\\[^\s\\]+(\\[^\s\\]+)*", RegexOptions.None, MatchTimeout), "[path]"),
        (new Regex(@"\b[A-Za-z]:[\\/][^\s]*", RegexOptions.None, MatchTimeout), "[path]"),
        (new Regex(@"(?<![\w.])(?:[\w.\-]+\\)+[\w.\-]+", RegexOptions.None, MatchTimeout), "[path]"),
        (new Regex(@"(?<![\w:/.])/(?:[\w.\-]+/)*[\w.\-]+", RegexOptions.None, MatchTimeout), "[path]"),
    ];

    /// <summary>
    /// What may be stored and shown: addresses, credentials and file system or remote paths replaced, then cut to
    /// <paramref name="maxLength"/>. The events are written from safe sentences already; this is the backstop that keeps
    /// a message that carried one from reaching the timeline.
    /// </summary>
    public static string Sanitize(string text, int maxLength)
    {
        foreach (var (pattern, replacement) in Redactions)
            text = pattern.Replace(text, replacement);
        return AcquisitionHistory.Truncate(text.Trim(), maxLength);
    }
}
