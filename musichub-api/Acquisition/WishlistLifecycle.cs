using System.Text.Json.Serialization;

namespace MusicHub.Api.Acquisition;

/// <summary>
/// Where a wanted track stands in getting a stored file. Each state is read from persisted facts (the stored files, the
/// download queue, the activity timeline) or from a search that was just scheduled; none is guessed.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WishlistState
{
    /// <summary>No stored file and no search has run for it.</summary>
    Missing,

    /// <summary>A search is scheduled or running.</summary>
    Searching,

    /// <summary>The latest search completed and found nothing worth downloading.</summary>
    NotFound,

    /// <summary>The latest search could not run, or the download failed for good; see the timeline.</summary>
    Failed,

    /// <summary>A download request is waiting for the worker.</summary>
    Queued,

    Downloading,

    /// <summary>The bytes arrived and wait to be identified and imported. Nothing is running for it yet.</summary>
    Downloaded,

    /// <summary>The track has a stored file.</summary>
    Available,
}

/// <summary>What the lifecycle needs to know about a track: the latest of each persisted source.</summary>
/// <param name="HasStoredAudio">Whether a stored audio file is linked to the track.</param>
/// <param name="Request">The status of the track's latest visible download request, if any.</param>
/// <param name="RequestAt">When that request last changed.</param>
/// <param name="LatestKind">The kind of the track's latest activity event, if any.</param>
/// <param name="LatestAt">When that event happened.</param>
/// <param name="SearchPending">A search was just scheduled for the track and has not started yet.</param>
public sealed record WishlistFacts(
    bool HasStoredAudio,
    DownloadRequestStatus? Request,
    DateTimeOffset? RequestAt,
    TrackActivityKind? LatestKind,
    DateTimeOffset? LatestAt,
    bool SearchPending);

/// <summary>The one owner of the mapping from persisted facts to a <see cref="WishlistState"/>.</summary>
public static class WishlistLifecycle
{
    /// <summary>
    /// A search whose start was recorded but whose outcome never was (the process stopped) stops counting as running
    /// after this long.
    /// </summary>
    public static readonly TimeSpan SearchStaleAfter = TimeSpan.FromMinutes(30);

    public static WishlistState Derive(WishlistFacts facts, DateTimeOffset now)
    {
        if (facts.HasStoredAudio)
            return WishlistState.Available;

        switch (facts.Request)
        {
            case DownloadRequestStatus.Queued: return WishlistState.Queued;
            case DownloadRequestStatus.Downloading: return WishlistState.Downloading;
            case DownloadRequestStatus.Downloaded: return WishlistState.Downloaded;
            // A failed download is the latest word only until a newer search says something else.
            case DownloadRequestStatus.Failed when !(facts.LatestAt > facts.RequestAt): return WishlistState.Failed;
        }

        if (facts.SearchPending || SearchRunning(facts, now))
            return WishlistState.Searching;

        return facts.LatestKind switch
        {
            TrackActivityKind.NotFound => WishlistState.NotFound,
            TrackActivityKind.SearchFailed => WishlistState.Failed,
            _ => WishlistState.Missing,
        };
    }

    /// <summary>Whether a new search may be scheduled: not while one is running or a download is under way.</summary>
    public static bool CanSearch(WishlistState state) =>
        state is WishlistState.Missing or WishlistState.NotFound or WishlistState.Failed;

    // A search started, or finished with candidates that are about to be queued, and nothing came after it.
    private static bool SearchRunning(WishlistFacts facts, DateTimeOffset now) =>
        facts.LatestKind is TrackActivityKind.SearchStarted or TrackActivityKind.SearchCompleted
        && facts.LatestAt is { } at && now - at < SearchStaleAfter;
}
