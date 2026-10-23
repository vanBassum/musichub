using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Jobs;

namespace MusicHub.Api.Acquisition;

/// <summary>One wanted track with its lifecycle state, the latest thing that happened and the queue entry it may have.</summary>
/// <param name="CanSearch">Whether Search now is offered: no stored file, no search running, no download under way.</param>
/// <param name="LastActivity">The sentence of the latest timeline event; null when nothing was ever recorded.</param>
/// <param name="DownloadRequestId">The track's visible download request, so Retry and Remove stay reachable from the row.</param>
public sealed record WishlistItem(
    int TrackId,
    string Title,
    string? Version,
    IReadOnlyList<string> Artists,
    WishlistState State,
    bool CanSearch,
    DateTimeOffset? LastActivityAt,
    string? LastActivity,
    int? DownloadRequestId,
    DownloadRequestStatus? DownloadStatus,
    string? Error);

/// <param name="Total">The items in the requested state (all states when none was asked for), not only this page.</param>
/// <param name="Counts">How many items each state has, over the whole overview.</param>
public sealed record WishlistPage(
    IReadOnlyList<WishlistItem> Items, int Total, IReadOnlyDictionary<WishlistState, int> Counts, int Page, int PageSize);

public enum SearchNowOutcome
{
    Scheduled,
    TrackNotFound,
    NoProvider,

    /// <summary>The track has a file, is being searched or downloaded; nothing was scheduled.</summary>
    NotEligible,

    JobsUnavailable,
}

public sealed record SearchNowResult(SearchNowOutcome Outcome, string? ExecutionId = null);

/// <summary>
/// The wishlist overview: the catalog's tracks that have no stored file, plus those whose acquisition is still visible in
/// the queue, each with a state derived from the stored files, the download queue and the activity timeline
/// (<see cref="WishlistLifecycle"/>). It owns no data of its own and adds no track: a track is on it because the catalog
/// says it is wanted. Search now goes through the existing Search for Missing Music run.
/// </summary>
public sealed class WishlistService(
    MusicHubDbContext db, JobService jobs, TrackSearchRequests searchRequests, IServiceProvider services, TimeProvider time)
{
    public const int MaxPageSize = 100;

    public async Task<WishlistPage> ListAsync(WishlistState? state, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var all = await LoadAsync(null, ct);
        var counts = all.GroupBy(i => i.State).ToDictionary(g => g.Key, g => g.Count());
        foreach (var each in Enum.GetValues<WishlistState>())
            counts.TryAdd(each, 0);

        var matching = all
            .Where(i => state is null || i.State == state)
            .OrderByDescending(i => i.LastActivityAt.HasValue)
            .ThenByDescending(i => i.LastActivityAt)
            .ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.TrackId)
            .ToList();
        return new WishlistPage(matching.Skip((page - 1) * pageSize).Take(pageSize).ToList(), matching.Count, counts, page, pageSize);
    }

    /// <summary>Schedules a search for one track unless it cannot be searched or one is already scheduled.</summary>
    public async Task<SearchNowResult> SearchNowAsync(int trackId, CancellationToken ct)
    {
        if (!MissingMusicAcquisition.AcquisitionProviderConfigured(services))
            return new SearchNowResult(SearchNowOutcome.NoProvider);
        if (!jobs.IsReady)
            return new SearchNowResult(SearchNowOutcome.JobsUnavailable);
        if ((await LoadAsync(trackId, ct)).SingleOrDefault() is not { } item)
            return new SearchNowResult(SearchNowOutcome.TrackNotFound);
        if (!item.CanSearch || !searchRequests.TryBegin(trackId))
            return new SearchNowResult(SearchNowOutcome.NotEligible);

        try
        {
            return new SearchNowResult(SearchNowOutcome.Scheduled, await jobs.EnqueueTrackSearchAsync(trackId, ct));
        }
        catch
        {
            searchRequests.Release(trackId);
            throw;
        }
    }

    // The latest of each source per track: the visible download request (an active one first), the latest timeline event.
    private async Task<List<WishlistItem>> LoadAsync(int? onlyTrackId, CancellationToken ct)
    {
        var requests = (await db.DownloadRequests.AsNoTracking()
                .Where(r => r.RemovedAt == null && r.TrackId != null && (onlyTrackId == null || r.TrackId == onlyTrackId))
                .Select(r => new { r.Id, TrackId = r.TrackId!.Value, r.Status, r.UpdatedAt, r.Error })
                .ToListAsync(ct))
            .GroupBy(r => r.TrackId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => DownloadRequestStatuses.Active.Contains(r.Status)).ThenByDescending(r => r.Id).First());

        var tracks = await db.Tracks.AsNoTracking()
            .Include(t => t.Artists).ThenInclude(a => a.Artist)
            .Where(t => (onlyTrackId == null || t.Id == onlyTrackId)
                && (!t.Files.AsQueryable().Any(TrackResponse.IsStoredAudio) || requests.Keys.Contains(t.Id)))
            .Select(t => new { Track = t, HasStoredAudio = t.Files.AsQueryable().Any(TrackResponse.IsStoredAudio) })
            .ToListAsync(ct);

        var ids = tracks.Select(t => t.Track.Id).ToList();
        var latest = (await db.TrackActivities.AsNoTracking()
                .Where(a => ids.Contains(a.TrackId) && !db.TrackActivities.Any(later => later.TrackId == a.TrackId && later.Id > a.Id))
                .Select(a => new { a.TrackId, a.Kind, a.OccurredAt, a.Summary })
                .ToListAsync(ct))
            .ToDictionary(a => a.TrackId);

        var now = time.GetUtcNow();
        return tracks.Select(t =>
        {
            var request = requests.GetValueOrDefault(t.Track.Id);
            var activity = latest.GetValueOrDefault(t.Track.Id);
            var state = WishlistLifecycle.Derive(
                new WishlistFacts(
                    t.HasStoredAudio, request?.Status, request?.UpdatedAt, activity?.Kind, activity?.OccurredAt,
                    searchRequests.IsPending(t.Track.Id)),
                now);
            // The request's own change counts as activity too: a queue entry can change without a timeline event.
            var lastAt = activity is null ? request?.UpdatedAt : request is null ? activity.OccurredAt
                : activity.OccurredAt > request.UpdatedAt ? activity.OccurredAt : request.UpdatedAt;
            return new WishlistItem(
                t.Track.Id, t.Track.Title, t.Track.Version,
                t.Track.Artists.OrderBy(a => a.Id).Select(a => a.Artist.Name).ToList(),
                state, WishlistLifecycle.CanSearch(state), lastAt, activity?.Summary,
                request?.Id, request?.Status, request?.Error);
        }).ToList();
    }
}
