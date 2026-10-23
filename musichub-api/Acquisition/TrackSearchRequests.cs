using System.Collections.Concurrent;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// The "Search now" requests that were scheduled and have not finished, so a second click does not schedule a second
/// search while the first has not yet written its start to the timeline. It lives in memory: after a restart Hangfire
/// still runs the scheduled job, and the timeline carries the search from then on. An entry that was never released
/// stops counting after <see cref="WishlistLifecycle.SearchStaleAfter"/>.
/// </summary>
public sealed class TrackSearchRequests(TimeProvider time)
{
    private readonly ConcurrentDictionary<int, DateTimeOffset> _pending = new();

    /// <summary>Marks a search as scheduled; false when one already is, in which case nothing is scheduled.</summary>
    public bool TryBegin(int trackId)
    {
        var now = time.GetUtcNow();
        while (true)
        {
            if (_pending.TryAdd(trackId, now))
                return true;
            if (!_pending.TryGetValue(trackId, out var since))
                continue;
            if (!IsStale(since, now))
                return false;
            if (_pending.TryUpdate(trackId, now, since))
                return true;
        }
    }

    public void Release(int trackId) => _pending.TryRemove(trackId, out _);

    public bool IsPending(int trackId) =>
        _pending.TryGetValue(trackId, out var since) && !IsStale(since, time.GetUtcNow());

    private static bool IsStale(DateTimeOffset since, DateTimeOffset now) => now - since >= WishlistLifecycle.SearchStaleAfter;
}
