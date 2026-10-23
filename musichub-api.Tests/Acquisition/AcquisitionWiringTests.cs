using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MusicHub.Api.Acquisition;

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
}
