using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Jobs;

namespace MusicHub.Api.Tests.Acquisition;

/// <summary>The lifecycle mapping, and the wishlist read model over real tables (stored files, queue, timeline).</summary>
public sealed class WishlistTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TrackSearchRequests _requests = new(TimeProvider.System);

    public WishlistTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private WishlistService Service(MusicHubDbContext db) => new(
        db,
        new JobService(new JobEngine(), new JobSchedule(Options.Create(new HangfireOptions()), NullLogger<JobSchedule>.Instance)),
        _requests,
        new NoServices(),
        TimeProvider.System);

    // No acquisition provider is registered, as when Slskd__BaseUrl is not set.
    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private int AddTrack(string title, bool stored = false)
    {
        using var db = CreateDb();
        var track = new Track { Title = title, CreatedAt = Now };
        track.Artists.Add(new TrackArtist { Artist = new Artist { Name = "Oasis", CreatedAt = Now } });
        if (stored)
            track.Files.Add(new FileRecord
            {
                OriginalFilename = "a.mp3", StorageLocation = "x", StorageKey = Guid.NewGuid().ToString(),
                Status = FileStatus.Stored, Source = "test",
            });
        db.Tracks.Add(track);
        db.SaveChanges();
        return track.Id;
    }

    private void AddActivity(int trackId, TrackActivityKind kind, DateTimeOffset? at = null)
    {
        using var db = CreateDb();
        db.TrackActivities.Add(new TrackActivity
        {
            TrackId = trackId, Kind = kind, OccurredAt = at ?? DateTimeOffset.UtcNow, Summary = kind.ToString(),
        });
        db.SaveChanges();
    }

    private int AddRequest(int trackId, DownloadRequestStatus status, DateTimeOffset? removedAt = null)
    {
        using var db = CreateDb();
        var request = new DownloadRequest
        {
            TrackId = trackId, Provider = "slskd", Source = "peer", RemotePath = $@"a\{Guid.NewGuid()}.mp3",
            Status = status, CreatedAt = Now, UpdatedAt = Now, RemovedAt = removedAt,
        };
        db.DownloadRequests.Add(request);
        db.SaveChanges();
        return request.Id;
    }

    private async Task<WishlistPage> ListAsync(WishlistState? state = null)
    {
        await using var db = CreateDb();
        return await Service(db).ListAsync(state, 1, 50, default);
    }

    private async Task<WishlistState> StateOfAsync(int trackId) =>
        (await ListAsync()).Items.Single(i => i.TrackId == trackId).State;

    private static WishlistState Derive(
        bool stored = false, DownloadRequestStatus? request = null, TrackActivityKind? kind = null,
        TimeSpan? age = null, bool pending = false, TimeSpan? requestAge = null) =>
        WishlistLifecycle.Derive(
            new WishlistFacts(
                stored, request, request is null ? null : Now - (requestAge ?? TimeSpan.Zero), kind,
                kind is null ? null : Now - (age ?? TimeSpan.Zero), pending),
            Now);

    [Fact]
    public void Lifecycle_maps_each_persisted_fact_to_its_state()
    {
        Assert.Equal(WishlistState.Missing, Derive());
        Assert.Equal(WishlistState.Available, Derive(stored: true, request: DownloadRequestStatus.Failed));
        Assert.Equal(WishlistState.Queued, Derive(request: DownloadRequestStatus.Queued));
        Assert.Equal(WishlistState.Downloading, Derive(request: DownloadRequestStatus.Downloading));
        Assert.Equal(WishlistState.Downloaded, Derive(request: DownloadRequestStatus.Downloaded));
        Assert.Equal(WishlistState.Failed, Derive(request: DownloadRequestStatus.Failed));
        Assert.Equal(WishlistState.NotFound, Derive(kind: TrackActivityKind.NotFound));
        Assert.Equal(WishlistState.Failed, Derive(kind: TrackActivityKind.SearchFailed));
        Assert.Equal(WishlistState.Searching, Derive(kind: TrackActivityKind.SearchStarted, age: TimeSpan.FromMinutes(1)));
        Assert.Equal(WishlistState.Searching, Derive(pending: true));
    }

    [Fact]
    public void A_search_that_never_reported_back_stops_counting_as_running()
    {
        var stale = WishlistLifecycle.SearchStaleAfter + TimeSpan.FromMinutes(1);
        Assert.Equal(WishlistState.Missing, Derive(kind: TrackActivityKind.SearchStarted, age: stale));
    }

    [Fact]
    public void A_downloaded_file_is_not_called_importing_and_a_newer_search_outranks_an_old_failure()
    {
        Assert.NotEqual(WishlistState.Searching, Derive(request: DownloadRequestStatus.Downloaded));
        Assert.Equal(
            WishlistState.NotFound,
            Derive(request: DownloadRequestStatus.Failed, kind: TrackActivityKind.NotFound, requestAge: TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Only_idle_tracks_can_be_searched()
    {
        var searchable = Enum.GetValues<WishlistState>().Where(WishlistLifecycle.CanSearch);
        Assert.Equal([WishlistState.Missing, WishlistState.NotFound, WishlistState.Failed], searchable);
    }

    [Fact]
    public async Task A_wanted_track_that_was_never_searched_is_missing_and_a_searched_one_without_candidates_is_not_found()
    {
        var never = AddTrack("Never");
        var empty = AddTrack("Empty");
        AddActivity(empty, TrackActivityKind.SearchStarted);
        AddActivity(empty, TrackActivityKind.NotFound);

        Assert.Equal(WishlistState.Missing, await StateOfAsync(never));
        Assert.Equal(WishlistState.NotFound, await StateOfAsync(empty));
        var item = (await ListAsync()).Items.Single(i => i.TrackId == empty);
        Assert.Equal("NotFound", item.LastActivity);
        Assert.True(item.CanSearch);
    }

    [Fact]
    public async Task The_queue_decides_downloading_and_the_state_survives_a_new_context()
    {
        var track = AddTrack("Downloading");
        AddRequest(track, DownloadRequestStatus.Downloading);

        Assert.Equal(WishlistState.Downloading, await StateOfAsync(track));
        var item = (await ListAsync()).Items.Single();
        Assert.False(item.CanSearch);
        Assert.Equal(DownloadRequestStatus.Downloading, item.DownloadStatus);
    }

    [Fact]
    public async Task Available_tracks_show_only_while_their_download_is_visible_and_never_twice()
    {
        var plain = AddTrack("Plain", stored: true);
        var acquired = AddTrack("Acquired", stored: true);
        AddRequest(acquired, DownloadRequestStatus.Completed);
        var removed = AddTrack("Removed", stored: true);
        AddRequest(removed, DownloadRequestStatus.Completed, removedAt: Now);

        var ids = (await ListAsync()).Items.Select(i => i.TrackId).ToList();
        Assert.Equal([acquired], ids);
        Assert.DoesNotContain(plain, ids);
        Assert.Equal(WishlistState.Available, await StateOfAsync(acquired));
    }

    [Fact]
    public async Task A_track_with_several_requests_is_listed_once_by_its_active_request()
    {
        var track = AddTrack("Twice");
        AddRequest(track, DownloadRequestStatus.Failed);
        AddRequest(track, DownloadRequestStatus.Queued);

        var page = await ListAsync();
        Assert.Equal(WishlistState.Queued, page.Items.Single().State);
    }

    [Fact]
    public async Task Counts_cover_every_state_while_the_list_is_filtered_and_paged()
    {
        AddTrack("A");
        AddTrack("B");
        var lost = AddTrack("C");
        AddActivity(lost, TrackActivityKind.NotFound);

        var filtered = await ListAsync(WishlistState.NotFound);
        Assert.Equal([lost], filtered.Items.Select(i => i.TrackId));
        Assert.Equal(1, filtered.Total);
        Assert.Equal(2, filtered.Counts[WishlistState.Missing]);
        Assert.Equal(1, filtered.Counts[WishlistState.NotFound]);
        Assert.Equal(0, filtered.Counts[WishlistState.Downloading]);

        await using var db = CreateDb();
        var paged = await Service(db).ListAsync(null, 2, 2, default);
        Assert.Single(paged.Items);
        Assert.Equal(3, paged.Total);
    }

    [Fact]
    public async Task An_empty_catalog_gives_an_empty_overview()
    {
        var page = await ListAsync();
        Assert.Empty(page.Items);
        Assert.All(page.Counts.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task A_scheduled_search_shows_as_searching_and_is_not_scheduled_twice()
    {
        var track = AddTrack("Pending");
        Assert.True(_requests.TryBegin(track));
        Assert.False(_requests.TryBegin(track));

        Assert.Equal(WishlistState.Searching, await StateOfAsync(track));
        _requests.Release(track);
        Assert.Equal(WishlistState.Missing, await StateOfAsync(track));
    }

    [Fact]
    public async Task Search_now_is_refused_without_a_provider_and_for_unknown_tracks()
    {
        var track = AddTrack("Idle");
        await using var db = CreateDb();
        var service = Service(db);
        var controller = new WishlistController(service);

        var noProvider = Assert.IsType<ObjectResult>(await controller.SearchNow(track, default));
        Assert.Equal(409, noProvider.StatusCode);
        Assert.False(_requests.IsPending(track));
    }

    [Fact]
    public async Task The_controller_lists_the_overview_with_the_state_filter()
    {
        AddTrack("A");
        await using var db = CreateDb();
        var controller = new WishlistController(Service(db));

        var ok = Assert.IsType<OkObjectResult>(await controller.List(WishlistState.Missing));
        Assert.Single(Assert.IsType<WishlistPage>(ok.Value).Items);
        var none = Assert.IsType<OkObjectResult>(await controller.List(WishlistState.Downloading));
        Assert.Empty(Assert.IsType<WishlistPage>(none.Value).Items);
    }
}
