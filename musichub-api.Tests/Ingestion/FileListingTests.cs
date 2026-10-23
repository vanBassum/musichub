using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Tests.Ingestion;

public sealed class FileListingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dbName = Guid.NewGuid().ToString();
    private int _clock;

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task<int> AddFileAsync(
        string name, FileStatus status = FileStatus.Stored, IdentificationSource? linkedBy = null,
        params IdentificationDecision[] attempts)
    {
        using var db = CreateDb();
        var file = new FileRecord
        {
            OriginalFilename = name, StorageLocation = "default", StorageKey = name, Status = status,
            Source = "upload", StartedAt = T0.AddMinutes(_clock++),
        };
        if (linkedBy is not null)
        {
            var track = new Track { Title = $"Track of {name}" };
            db.Tracks.Add(track);
            await db.SaveChangesAsync();
            file.TrackId = track.Id;
        }
        db.Files.Add(file);
        await db.SaveChangesAsync();
        foreach (var decision in attempts)
            db.IdentificationOutcomes.Add(new IdentificationOutcome
            {
                JobExecutionId = "expired-1", FileId = file.Id, FileName = name, Decision = decision,
                Reason = decision.ToString(), RecordedAt = T0.AddMinutes(_clock++),
            });
        if (linkedBy is { } source)
            db.AppliedIdentifications.Add(new AppliedIdentification
            {
                FileId = file.Id, TrackId = file.TrackId, Source = source, AppliedAt = T0.AddMinutes(_clock++),
            });
        await db.SaveChangesAsync();
        return file.Id;
    }

    private async Task<FileListPage> ListAsync(
        FileStatus? status = null, FileIdentificationFilter? identification = null, int page = 1, int pageSize = 50,
        string? search = null)
    {
        using var db = CreateDb();
        return await new FileListing(db).ListAsync(status, identification, page, pageSize, CancellationToken.None, search);
    }

    private async Task<FileCounts> CountAsync()
    {
        using var db = CreateDb();
        return await new FileListing(db).CountAsync(CancellationToken.None);
    }

    private async Task SeedMixAsync()
    {
        await AddFileAsync("linked-auto.mp3", linkedBy: IdentificationSource.Automatic, attempts: IdentificationDecision.Identified);
        await AddFileAsync("linked-manual.mp3", linkedBy: IdentificationSource.Manual);
        await AddFileAsync("fresh.mp3");
        await AddFileAsync("uncertain.mp3", attempts: IdentificationDecision.InsufficientEvidence);
        await AddFileAsync("nothing.mp3", attempts: IdentificationDecision.NoMatch);
        await AddFileAsync("broke.mp3", attempts: [IdentificationDecision.NoMatch, IdentificationDecision.Error]);
        await AddFileAsync("skipped.mp3", attempts: IdentificationDecision.Skipped);
        await AddFileAsync("upload-failed.mp3", FileStatus.Failed);
    }

    [Fact]
    public async Task Filters_SelectFilesByTheirLatestRecordedState()
    {
        await SeedMixAsync();

        async Task<string[]> Names(FileIdentificationFilter filter) =>
            (await ListAsync(identification: filter)).Items.Select(r => r.File.OriginalFilename).Order().ToArray();

        Assert.Equal(["linked-auto.mp3", "linked-manual.mp3"], await Names(FileIdentificationFilter.Identified));
        Assert.Equal(["linked-manual.mp3"], await Names(FileIdentificationFilter.ManuallyConfirmed));
        Assert.Equal(["broke.mp3", "fresh.mp3", "nothing.mp3", "skipped.mp3", "uncertain.mp3"], await Names(FileIdentificationFilter.Unidentified));
        Assert.Equal(["fresh.mp3", "skipped.mp3"], await Names(FileIdentificationFilter.NotAttempted));
        Assert.Equal(["uncertain.mp3"], await Names(FileIdentificationFilter.Uncertain));
        Assert.Equal(["nothing.mp3"], await Names(FileIdentificationFilter.NoMatch));
        Assert.Equal(["broke.mp3"], await Names(FileIdentificationFilter.Error));
    }

    [Fact]
    public async Task Counts_PartitionStoredFilesAndMatchTheFilteredTotals()
    {
        await SeedMixAsync();

        var counts = await CountAsync();

        Assert.Equal(8, counts.Total);
        Assert.Equal(7, counts.Statuses[FileStatus.Stored]);
        Assert.Equal(1, counts.Statuses[FileStatus.Failed]);
        var states = counts.Identification;
        Assert.Equal(counts.Statuses[FileStatus.Stored], states[FileIdentificationFilter.Identified] + states[FileIdentificationFilter.Unidentified]);
        Assert.Equal(
            states[FileIdentificationFilter.Unidentified],
            Enum.GetValues<FileIdentificationFilter>()
                .Where(s => s is not (FileIdentificationFilter.Identified or FileIdentificationFilter.ManuallyConfirmed or FileIdentificationFilter.Unidentified))
                .Sum(s => states[s]));
        foreach (var (state, count) in states)
            Assert.Equal(count, (await ListAsync(identification: state)).Total);
    }

    [Fact]
    public async Task Total_CountsEveryMatchWhilePagingOnlyReturnsOnePage()
    {
        for (var i = 0; i < 5; i++)
            await AddFileAsync($"u{i}.mp3");
        await AddFileAsync("linked.mp3", linkedBy: IdentificationSource.Automatic);

        var second = await ListAsync(identification: FileIdentificationFilter.Unidentified, page: 2, pageSize: 2);

        Assert.Equal(5, second.Total);
        Assert.Equal(["u2.mp3", "u1.mp3"], second.Items.Select(r => r.File.OriginalFilename));
        Assert.Equal(2, second.Page);
    }

    [Fact]
    public async Task StatusAndIdentificationCombine_AndOnlyStoredFilesCarryIdentification()
    {
        await SeedMixAsync();

        var failed = await ListAsync(status: FileStatus.Failed);
        var uncertainStored = await ListAsync(FileStatus.Stored, FileIdentificationFilter.Uncertain);
        var uncertainFailed = await ListAsync(FileStatus.Failed, FileIdentificationFilter.Uncertain);

        Assert.Null(Assert.Single(failed.Items).Identification);
        Assert.Equal(1, uncertainStored.Total);
        Assert.Equal(0, uncertainFailed.Total);
    }

    [Fact]
    public async Task Search_MatchesFilenamesAnywhereInTheListAndNarrowsTotalAndCounts()
    {
        for (var i = 0; i < 4; i++)
            await AddFileAsync($"Beatles-{i}.mp3");
        await AddFileAsync("beatles-broken.mp3", FileStatus.Failed);
        await AddFileAsync("other.mp3");

        var second = await ListAsync(page: 2, pageSize: 2, search: "  BEATLES ");
        var combined = await ListAsync(FileStatus.Failed, search: "beatles");
        var blank = await ListAsync(search: "  ");
        using var db = CreateDb();
        var counts = await new FileListing(db).CountAsync(CancellationToken.None, "beatles");

        Assert.Equal(5, second.Total);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal("beatles-broken.mp3", Assert.Single(combined.Items).File.OriginalFilename);
        Assert.Equal(6, blank.Total);
        Assert.Equal(5, counts.Total);
        Assert.Equal(1, counts.Statuses[FileStatus.Failed]);
        Assert.Equal(4, counts.Identification[FileIdentificationFilter.NotAttempted]);
    }

    [Fact]
    public async Task Items_ShowTheLatestAttemptAndWhoLinkedTheFile()
    {
        await SeedMixAsync();

        var items = (await ListAsync()).Items.ToDictionary(r => r.File.OriginalFilename, r => r.Identification);

        Assert.Equal(FileIdentificationFilter.ManuallyConfirmed, items["linked-manual.mp3"]!.State);
        Assert.Equal(IdentificationSource.Manual, items["linked-manual.mp3"]!.Source);
        Assert.Equal("Track of linked-manual.mp3", items["linked-manual.mp3"]!.TrackTitle);
        Assert.Equal(FileIdentificationFilter.Identified, items["linked-auto.mp3"]!.State);
        Assert.Equal(FileIdentificationFilter.Error, items["broke.mp3"]!.State);
        Assert.Equal(IdentificationDecision.Error, items["broke.mp3"]!.Decision);
        Assert.Equal(FileIdentificationFilter.NotAttempted, items["fresh.mp3"]!.State);
        Assert.Null(items["fresh.mp3"]!.Decision);
    }

    [Fact]
    public async Task History_KeepsEveryAttemptAndAppliedIdentityNewestFirst()
    {
        var id = await AddFileAsync(
            "a.mp3", linkedBy: IdentificationSource.Manual,
            attempts: [IdentificationDecision.InsufficientEvidence, IdentificationDecision.Error]);
        using var db = CreateDb();
        var listing = new FileListing(db);

        var history = await listing.HistoryAsync(id, CancellationToken.None);

        Assert.NotNull(history);
        Assert.Equal([IdentificationDecision.Error, IdentificationDecision.InsufficientEvidence], history.Attempts.Select(a => a.Decision));
        Assert.Equal(IdentificationSource.Manual, Assert.Single(history.Applied).Source);
        Assert.Null(await listing.HistoryAsync(id + 99, CancellationToken.None));
    }
}
