using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Tests.Ingestion;

namespace MusicHub.Api.Tests.Catalog;

public sealed class ArtistExternalLinksTests : IDisposable
{
    private const string MbA = "11111111-1111-1111-1111-111111111111";
    private const string MbB = "22222222-2222-2222-2222-222222222222";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeFileStorage _storage = new();
    private readonly Dictionary<string, IdentificationResult> _identities = [];

    public ArtistExternalLinksTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private sealed class ByNameIdentifier(Dictionary<string, IdentificationResult> identities) : IRecordingIdentifier
    {
        public Task<IdentificationResult> IdentifyAsync(Stream audio, string? fileNameHint, CancellationToken ct) =>
            Task.FromResult(identities[fileNameHint!]);
    }

    private static ArtistReference Mb(string id, string name) => new(ExternalSources.MusicBrainz, id, name);

    private void Identify(string file, string title, string[] artists, params ArtistReference[] references) =>
        _identities[file] = IdentificationResult.Identified(
            new ProposedRecordingIdentity(title, artists, null, references), [new("musicbrainz", "recording")]);

    private async Task ReconcileAsync(string file)
    {
        using var db = CreateDb();
        var record = new FileRecord
        {
            OriginalFilename = file,
            StorageLocation = "default",
            StorageKey = $"library/aa/{Guid.NewGuid()}",
            Status = FileStatus.Stored,
            Source = "upload",
            StartedAt = DateTimeOffset.UtcNow,
        };
        db.Files.Add(record);
        await db.SaveChangesAsync();
        await _storage.PutAsync(record.StorageKey, new MemoryStream([1]), null, CancellationToken.None);

        var service = new CatalogReconciliation(
            db, _storage, new ByNameIdentifier(_identities), TimeProvider.System, NullLogger<CatalogReconciliation>.Instance);
        var summary = await service.RunAsync(CancellationToken.None);
        Assert.Equal(0, summary.Failed);
    }

    private async Task<int> SeedArtistAsync(string name)
    {
        using var db = CreateDb();
        var artist = new Artist { Name = name, CreatedAt = DateTimeOffset.UtcNow };
        db.Artists.Add(artist);
        await db.SaveChangesAsync();
        return artist.Id;
    }

    private async Task<List<ArtistExternalLink>> LinksAsync()
    {
        using var db = CreateDb();
        return await db.ArtistExternalLinks.AsNoTracking().OrderBy(l => l.Id).ToListAsync();
    }

    private ArtistExternalLinksController Controller(MusicHubDbContext db) => new(db, TimeProvider.System);

    [Fact]
    public async Task A_credited_identifier_is_linked_automatically_with_its_evidence()
    {
        Identify("a.mp3", "Song", ["Artist"], Mb(MbA, "Artist"));

        await ReconcileAsync("a.mp3");

        var link = Assert.Single(await LinksAsync());
        Assert.Equal((ExternalSources.MusicBrainz, MbA), (link.Source, link.ExternalId));
        Assert.Equal((ExternalLinkStatus.Linked, ExternalLinkOrigin.Automatic), (link.Status, link.Origin));
        Assert.Contains("musicbrainz", link.Evidence);
        using var db = CreateDb();
        Assert.Equal((await db.Artists.SingleAsync()).Id, link.ArtistId);
    }

    [Fact]
    public async Task A_name_without_an_identifier_is_never_linked()
    {
        Identify("a.mp3", "Song", ["Artist"]);

        await ReconcileAsync("a.mp3");

        Assert.Empty(await LinksAsync());
    }

    [Fact]
    public async Task An_existing_artist_keeps_its_record_and_gains_the_link()
    {
        var existing = await SeedArtistAsync("Artist");
        Identify("a.mp3", "Song", ["artist"], Mb(MbA, "artist"));

        await ReconcileAsync("a.mp3");

        using var db = CreateDb();
        Assert.Equal(existing, (await db.Artists.SingleAsync()).Id);
        Assert.Equal(existing, Assert.Single(await LinksAsync()).ArtistId);
    }

    [Fact]
    public async Task An_existing_track_gains_links_without_changing()
    {
        Identify("a.mp3", "Song", ["Artist"]);
        await ReconcileAsync("a.mp3");
        Identify("b.mp3", "Song", ["Artist"], Mb(MbA, "Artist"));

        await ReconcileAsync("b.mp3");

        using var db = CreateDb();
        Assert.Equal(1, await db.Tracks.CountAsync());
        Assert.Equal(1, await db.Artists.CountAsync());
        Assert.Single(await LinksAsync());
    }

    [Fact]
    public async Task The_same_identifier_under_another_credited_name_is_the_same_artist()
    {
        Identify("a.mp3", "One", ["The Band"], Mb(MbA, "The Band"));
        Identify("b.mp3", "Two", ["Band, The"], Mb(MbA, "Band, The"));

        await ReconcileAsync("a.mp3");
        await ReconcileAsync("b.mp3");

        using var db = CreateDb();
        Assert.Equal("The Band", (await db.Artists.SingleAsync()).Name);
        Assert.Single(await LinksAsync());
        Assert.Equal(2, await db.TrackArtists.CountAsync());
    }

    [Fact]
    public async Task A_same_name_with_a_different_identifier_is_another_artist()
    {
        Identify("a.mp3", "One", ["Echo"], Mb(MbA, "Echo"));
        Identify("b.mp3", "Two", ["Echo"], Mb(MbB, "Echo"));

        await ReconcileAsync("a.mp3");
        await ReconcileAsync("b.mp3");

        using var db = CreateDb();
        Assert.Equal(2, await db.Artists.CountAsync());
        var links = await LinksAsync();
        Assert.Equal(2, links.Count);
        Assert.NotEqual(links[0].ArtistId, links[1].ArtistId);
    }

    [Fact]
    public async Task An_ambiguous_name_match_makes_no_link_and_adds_no_artist()
    {
        var oldest = await SeedArtistAsync("Echo");
        await SeedArtistAsync("Echo");
        Identify("a.mp3", "Song", ["Echo"], Mb(MbA, "Echo"));

        await ReconcileAsync("a.mp3");

        Assert.Empty(await LinksAsync());
        using var db = CreateDb();
        Assert.Equal(2, await db.Artists.CountAsync());
        Assert.Equal(oldest, (await db.TrackArtists.SingleAsync()).ArtistId);
    }

    [Fact]
    public async Task An_unlinked_correction_is_not_reverted_by_a_later_run()
    {
        Identify("a.mp3", "One", ["Echo"], Mb(MbA, "Echo"));
        await ReconcileAsync("a.mp3");
        var wrong = Assert.Single(await LinksAsync());
        using (var db = CreateDb())
        {
            Assert.IsType<OkObjectResult>(await Controller(db).Unlink(wrong.ArtistId, wrong.Id, default));
        }
        Identify("b.mp3", "One", ["Echo"], Mb(MbA, "Echo"));
        Identify("c.mp3", "Three", ["Echo"], Mb(MbA, "Echo"));

        await ReconcileAsync("b.mp3");
        await ReconcileAsync("c.mp3");

        var links = await LinksAsync();
        Assert.All(links.Where(l => l.ArtistId == wrong.ArtistId), l =>
            Assert.Equal((ExternalLinkStatus.Unlinked, ExternalLinkOrigin.Manual), (l.Status, l.Origin)));
        Assert.Single(links, l => l.ArtistId == wrong.ArtistId);
        Assert.NotEqual(wrong.ArtistId, Assert.Single(links, l => l.Status == ExternalLinkStatus.Linked).ArtistId);
    }

    [Fact]
    public async Task A_manual_link_is_not_replaced_by_a_different_automatic_identifier()
    {
        var artist = await SeedArtistAsync("Echo");
        using (var db = CreateDb())
        {
            Assert.IsType<OkObjectResult>(await Controller(db).Link(
                artist, new LinkArtistExternalIdRequest("MusicBrainz", MbA.ToUpperInvariant()), default));
        }
        Identify("a.mp3", "Song", ["Echo"], Mb(MbB, "Echo"));

        await ReconcileAsync("a.mp3");

        var links = await LinksAsync();
        var manual = Assert.Single(links, l => l.ArtistId == artist);
        Assert.Equal((MbA, ExternalLinkOrigin.Manual), (manual.ExternalId, manual.Origin));
        using var read = CreateDb();
        var track = await read.TrackArtists.SingleAsync();
        Assert.NotEqual(artist, track.ArtistId);
    }

    [Fact]
    public async Task Links_list_link_conflict_and_unlink_through_the_api()
    {
        var first = await SeedArtistAsync("One");
        var second = await SeedArtistAsync("Two");
        using var db = CreateDb();
        var controller = Controller(db);

        Assert.IsType<NotFoundResult>(await controller.List(999, default));
        Assert.IsType<NotFoundResult>(await controller.Link(999, new("musicbrainz", MbA), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Link(first, new("musicbrainz", "not-a-uuid"), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Link(first, new(null, null), default));

        var linked = Assert.IsType<ArtistExternalLinkResponse>(
            Assert.IsType<OkObjectResult>(await controller.Link(first, new("musicbrainz", MbA), default)).Value);
        Assert.Equal($"https://musicbrainz.org/artist/{MbA}", linked.Url);
        Assert.Equal(("Linked", "Manual"), (linked.Status, linked.Origin));

        // The identifier is the first artist's; another artist cannot take it, and the first has one already.
        Assert.IsType<ConflictObjectResult>(await controller.Link(second, new("musicbrainz", MbA), default));
        Assert.IsType<ConflictObjectResult>(await controller.Link(first, new("musicbrainz", MbB), default));
        Assert.IsType<NotFoundResult>(await controller.Unlink(second, linked.Id, default));

        var unlinked = Assert.IsType<ArtistExternalLinkResponse>(
            Assert.IsType<OkObjectResult>(await controller.Unlink(first, linked.Id, default)).Value);
        Assert.Equal("Unlinked", unlinked.Status);

        // Corrected: the right artist takes it, and the row of the wrong one stays as the record of the correction.
        Assert.IsType<OkObjectResult>(await controller.Link(second, new("musicbrainz", MbA), default));
        var list = Assert.IsType<ArtistExternalLinkListResponse>(
            Assert.IsType<OkObjectResult>(await controller.List(first, default)).Value);
        Assert.Equal("Unlinked", Assert.Single(list.Items).Status);

        // Relinking what was unlinked works once nothing else holds it.
        Assert.IsType<OkObjectResult>(await controller.Unlink(second, (await LinksAsync()).Last().Id, default));
        var relinked = Assert.IsType<ArtistExternalLinkResponse>(
            Assert.IsType<OkObjectResult>(await controller.Link(first, new("musicbrainz", MbA), default)).Value);
        Assert.Equal(linked.Id, relinked.Id);
        Assert.Equal("Linked", relinked.Status);
    }

    [Fact]
    public async Task One_artist_can_have_identifiers_of_several_sources_and_sources_scope_identifiers()
    {
        var first = await SeedArtistAsync("One");
        var second = await SeedArtistAsync("Two");
        using var db = CreateDb();
        var controller = Controller(db);

        Assert.IsType<OkObjectResult>(await controller.Link(first, new("musicbrainz", MbA), default));
        Assert.IsType<OkObjectResult>(await controller.Link(first, new("spotify", "4Z8W4fKeB5YxbusRsdQVPb"), default));
        // The same identifier text under another source is a different identifier.
        Assert.IsType<OkObjectResult>(await controller.Link(second, new("deezer", "4Z8W4fKeB5YxbusRsdQVPb"), default));

        var list = Assert.IsType<ArtistExternalLinkListResponse>(
            Assert.IsType<OkObjectResult>(await controller.List(first, default)).Value);
        Assert.Equal(["musicbrainz", "spotify"], list.Items.Select(i => i.Source));
        Assert.Equal("", list.Items[1].Url);
    }

    [Fact]
    public async Task The_database_refuses_an_identifier_linked_to_two_artists()
    {
        var first = await SeedArtistAsync("One");
        var second = await SeedArtistAsync("Two");
        using var db = CreateDb();
        foreach (var artistId in new[] { first, second })
        {
            db.ArtistExternalLinks.Add(new ArtistExternalLink
            {
                ArtistId = artistId, Source = "musicbrainz", ExternalId = MbA,
                Status = ExternalLinkStatus.Linked, Origin = ExternalLinkOrigin.Manual,
            });
        }

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
