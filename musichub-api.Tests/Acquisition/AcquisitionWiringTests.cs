using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Api.Acquisition;
using MusicHub.Api.Data;
using MusicHub.Api.Ingestion;
using MusicHub.Api.Tests.Ingestion;

namespace MusicHub.Api.Tests.Acquisition;

public class AcquisitionWiringTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddAcquisition(configuration).BuildServiceProvider();
    }

    private sealed class HeaderCapture : HttpMessageHandler
    {
        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ApiKey = request.Headers.GetValues("X-API-Key").Single();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }

    [Fact]
    public void Without_a_slskd_url_no_provider_is_registered()
    {
        using var services = Build([]);

        Assert.Null(services.GetService<IAcquisitionProvider>());
    }

    [Fact]
    public async Task A_configured_slskd_is_the_acquisition_provider_and_sends_the_key_in_a_header()
    {
        var capture = new HeaderCapture();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Slskd:BaseUrl"] = "http://slskd:5030",
            ["Slskd:ApiKey"] = "secret-key",
        }).Build();
        var collection = new ServiceCollection().AddAcquisition(configuration);
        collection.AddHttpClient<IAcquisitionProvider, MusicHub.Api.Acquisition.Slskd.SlskdAcquisitionProvider>()
            .ConfigurePrimaryHttpMessageHandler(() => capture);
        using var services = collection.BuildServiceProvider();

        var provider = services.GetRequiredService<IAcquisitionProvider>();
        await Assert.ThrowsAsync<AcquisitionUnavailableException>(
            () => provider.SearchAsync(new AcquisitionQuery("a", "b"), CancellationToken.None));

        Assert.Equal("slskd", provider.Name);
        Assert.Equal("secret-key", capture.ApiKey);
    }

    [Fact]
    public void A_slskd_url_without_an_api_key_fails_loudly_naming_the_setting()
    {
        using var services = Build(new() { ["Slskd:BaseUrl"] = "http://slskd:5030" });

        var e = Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<IAcquisitionProvider>());

        Assert.Contains("Slskd__ApiKey", e.Message);
    }

    [Fact]
    public async Task A_completed_download_is_stored_and_registered_with_its_provider_as_source()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            var options = new DbContextOptionsBuilder<MusicHubDbContext>().UseInMemoryDatabase(nameof(A_completed_download_is_stored_and_registered_with_its_provider_as_source)).Options;
            await using var db = new MusicHubDbContext(options);
            var storage = new FakeFileStorage();
            var handoff = new AcquisitionHandoff(new FileIngestionService(db, storage, new ActiveUploadTracker()));

            var record = await handoff.IngestAsync(
                AcquisitionDownloadOutcome.Completed("slskd", new LocalAcquiredFile("01 A Title.mp3", path)), CancellationToken.None);

            Assert.Equal(FileStatus.Stored, record.Status);
            Assert.Equal("acquisition:slskd", record.Source);
            Assert.Equal("01 A Title.mp3", record.OriginalFilename);
            Assert.Equal(3, record.SizeBytes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_failed_download_cannot_be_handed_to_ingestion()
    {
        var options = new DbContextOptionsBuilder<MusicHubDbContext>().UseInMemoryDatabase(nameof(A_failed_download_cannot_be_handed_to_ingestion)).Options;
        await using var db = new MusicHubDbContext(options);
        var handoff = new AcquisitionHandoff(new FileIngestionService(db, new FakeFileStorage(), new ActiveUploadTracker()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handoff.IngestAsync(
            AcquisitionDownloadOutcome.Failed("slskd", AcquisitionFailure.Rejected, "no"), CancellationToken.None));
    }
}
