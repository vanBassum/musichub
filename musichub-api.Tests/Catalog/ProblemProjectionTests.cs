using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Jobs;
using MusicHub.Api.Problems;
using Xunit;

namespace MusicHub.Api.Tests.Catalog;

public sealed class ProblemProjectionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ProblemProjectionTests()
    {
        _connection.Open();
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private MusicHubDbContext CreateDb() => new(
        new DbContextOptionsBuilder<MusicHubDbContext>().UseSqlite(_connection).Options);

    private async Task<int> SeedFile(string name, FileStatus status = FileStatus.Stored, bool linked = false)
    {
        using var db = CreateDb();
        var file = new FileRecord
        {
            OriginalFilename = name, StorageLocation = "default", StorageKey = $"library/{name}", Source = "upload", Status = status,
        };
        if (linked)
            file.Track = new Track { Title = name, CreatedAt = DateTimeOffset.UtcNow };
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return file.Id;
    }

    private async Task Outcome(int fileId, IdentificationDecision decision, string execution = "1", string reason = "why")
    {
        using var db = CreateDb();
        db.IdentificationOutcomes.Add(new IdentificationOutcome
        {
            FileId = fileId, FileName = $"file{fileId}", JobExecutionId = execution, Decision = decision, Reason = reason,
            RecordedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<Problem>> Unresolved()
    {
        using var db = CreateDb();
        return await ProblemProjection.UnresolvedIdentificationsAsync(db, default);
    }

    [Fact]
    public async Task Unresolved_ListsUnlinkedStoredFiles_WithSourceDetailLink()
    {
        var id = await SeedFile("a.mp3");
        await Outcome(id, IdentificationDecision.InsufficientEvidence, execution: "7", reason: "Candidates conflict.");

        var problem = Assert.Single(await Unresolved());

        Assert.Equal(ProblemSources.Identification, problem.Source);
        Assert.Equal("InsufficientEvidence", problem.Kind);
        Assert.Equal("Candidates conflict.", problem.Reason);
        Assert.Equal("7", problem.JobExecutionId);
        Assert.Equal(id, problem.FileId);
    }

    [Fact]
    public async Task Unresolved_UsesOnlyTheLatestAttemptOfAFile()
    {
        var id = await SeedFile("a.mp3");
        await Outcome(id, IdentificationDecision.NoMatch, execution: "1");
        await Outcome(id, IdentificationDecision.MethodFailed, execution: "2");

        var problem = Assert.Single(await Unresolved());

        Assert.Equal("MethodFailed", problem.Kind);
        Assert.Equal("2", problem.JobExecutionId);
    }

    [Fact]
    public async Task Unresolved_IgnoresLinkedSkippedIdentifiedAndNotStoredFiles()
    {
        var linked = await SeedFile("linked.mp3", linked: true);
        await Outcome(linked, IdentificationDecision.NoMatch);
        var skipped = await SeedFile("skipped.mp3");
        await Outcome(skipped, IdentificationDecision.Skipped);
        var identified = await SeedFile("identified.mp3");
        await Outcome(identified, IdentificationDecision.Identified);
        var failedUpload = await SeedFile("failed.mp3", FileStatus.Failed);
        await Outcome(failedUpload, IdentificationDecision.NoMatch);

        Assert.Empty(await Unresolved());
    }

    [Fact]
    public async Task Unresolved_AFileLaterIdentified_IsNoLongerAProblem()
    {
        var id = await SeedFile("a.mp3");
        await Outcome(id, IdentificationDecision.NoMatch, execution: "1");
        await Outcome(id, IdentificationDecision.Identified, execution: "2");

        Assert.Empty(await Unresolved());
    }

    private static JobExecutionDetails Execution(string id, string state, string? errorType, string? error = "secret detail") =>
        new(
            new JobExecution(id, state, new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc), null,
                new DateTime(2026, 10, 20, 10, 5, 0, DateTimeKind.Utc), null, error),
            [],
            errorType);

    [Fact]
    public void FailedExecutions_KeepOnlyFailed_NameTheTypeButNeverTheMessage()
    {
        var problems = ProblemProjection.FailedExecutions([
            Execution("5", "Failed", "System.InvalidOperationException"),
            Execution("6", "Succeeded", null, error: null),
        ]);

        var problem = Assert.Single(problems);
        Assert.Equal(ProblemSources.Job, problem.Source);
        Assert.Equal("5", problem.JobExecutionId);
        Assert.Contains("InvalidOperationException", problem.Reason);
        Assert.DoesNotContain("secret detail", problem.Reason);
        Assert.DoesNotContain("System.", problem.Reason);
    }
}
