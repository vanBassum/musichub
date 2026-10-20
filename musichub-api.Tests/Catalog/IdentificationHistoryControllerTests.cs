using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Controllers;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Tests.Catalog;

public sealed class IdentificationHistoryControllerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public IdentificationHistoryControllerTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private async Task SeedAsync(params IdentificationDecision[] decisions)
    {
        using var db = CreateDb();
        var file = new FileRecord
        {
            OriginalFilename = "a.mp3", StorageLocation = "default", StorageKey = "k", Status = FileStatus.Stored,
            Source = "upload", StartedAt = DateTimeOffset.UtcNow,
        };
        db.Files.Add(file);
        await db.SaveChangesAsync();
        foreach (var decision in decisions)
            db.IdentificationOutcomes.Add(new IdentificationOutcome
            {
                // An execution id that Hangfire no longer has: the history must not depend on it.
                JobExecutionId = "expired-1", FileId = file.Id, FileName = "a.mp3", Decision = decision,
                Reason = decision.ToString(), RecordedAt = DateTimeOffset.UtcNow,
            });
        await db.SaveChangesAsync();
    }

    private async Task<IdentificationHistoryController.OutcomePage> ListAsync(
        IdentificationDecision? decision = null, int page = 1, int pageSize = 25)
    {
        using var db = CreateDb();
        var result = await new IdentificationHistoryController(db).List(decision, page, pageSize, default);
        return Assert.IsType<IdentificationHistoryController.OutcomePage>(Assert.IsType<OkObjectResult>(result).Value);
    }

    [Fact]
    public async Task By_default_lists_only_unresolved_outcomes_newest_first_without_needing_the_execution()
    {
        await SeedAsync(
            IdentificationDecision.Identified, IdentificationDecision.NoMatch,
            IdentificationDecision.Skipped, IdentificationDecision.InsufficientEvidence);

        var page = await ListAsync();

        Assert.Equal(2, page.Total);
        Assert.Equal(
            [IdentificationDecision.InsufficientEvidence, IdentificationDecision.NoMatch],
            page.Items.Select(i => i.Decision));
        Assert.All(page.Items, i => Assert.Equal("expired-1", i.JobExecutionId));
    }

    [Fact]
    public async Task A_decision_filter_also_shows_linked_outcomes()
    {
        await SeedAsync(IdentificationDecision.Identified, IdentificationDecision.NoMatch);

        var page = await ListAsync(IdentificationDecision.Identified);

        Assert.Equal(IdentificationDecision.Identified, Assert.Single(page.Items).Decision);
    }

    [Fact]
    public async Task Pages_are_bounded_and_counted()
    {
        await SeedAsync(Enumerable.Repeat(IdentificationDecision.NoMatch, 3).ToArray());

        var page = await ListAsync(page: 2, pageSize: 2);

        Assert.Equal(3, page.Total);
        Assert.Single(page.Items);
        Assert.Equal(2, page.Page);
    }
}
