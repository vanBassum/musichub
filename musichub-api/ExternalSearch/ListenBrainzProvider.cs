using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MusicHub.Api.Identification;

namespace MusicHub.Api.ExternalSearch;

/// <summary>Binds the <c>ListenBrainz</c> configuration section (environment variables <c>ListenBrainz__*</c>).</summary>
public sealed class ListenBrainzOptions
{
    public const string SectionName = "ListenBrainz";

    /// <summary>Popularity enrichment is off unless enabled. The popularity endpoint is public: no token is needed.</summary>
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://api.listenbrainz.org/1/";

    /// <summary>How ListenBrainz can reach whoever runs this (an email address or URL); sent in the User-Agent when set.</summary>
    public string? Contact { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    public int MinRequestIntervalMs { get; set; } = 500;

    public int MaxRetries { get; set; } = 2;

    public int RetryDelayMs { get; set; } = 2000;

    public int MaxRetryDelayMs { get; set; } = 10000;

    /// <summary>How long an artist's popularity is reused before ListenBrainz is asked again.</summary>
    public int CacheMinutes { get; set; } = 360;
}

/// <summary>
/// ListenBrainz as an external metadata provider: it supplies popularity by MusicBrainz recording identifier and
/// nothing else, so every identity and search method answers empty. Answers are cached per artist (failures are
/// not); a failure throws <see cref="CandidateSearchUnavailableException"/>, never an empty result.
/// </summary>
public sealed class ListenBrainzProvider : IExternalMusicSearch
{
    public const string ProviderName = "listenbrainz";

    private readonly RateLimitedJsonClient client;
    private readonly IMemoryCache cache;
    private readonly TimeSpan cacheFor;

    public ListenBrainzProvider(HttpClient http, ListenBrainzOptions options, IMemoryCache cache)
    {
        this.cache = cache;
        cacheFor = TimeSpan.FromMinutes(options.CacheMinutes);
        client = new RateLimitedJsonClient(http, new RateLimitedJsonSettings(
            "ListenBrainz", options.TimeoutSeconds, options.MinRequestIntervalMs, options.MaxRetries,
            options.RetryDelayMs, options.MaxRetryDelayMs));
    }

    public string Provider => ProviderName;

    /// <summary>Builds the provider from configuration; throws when the options cannot work, so a bad setup is loud.</summary>
    public static ListenBrainzProvider Create(ListenBrainzOptions options, IHttpClientFactory factory, IMemoryCache cache)
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl))
        {
            throw new InvalidOperationException("ListenBrainz__BaseUrl must be an absolute URL.");
        }
        if (options.TimeoutSeconds < 1 || options.MaxRetries < 0 || options.MinRequestIntervalMs < 0
            || options.RetryDelayMs < 0 || options.MaxRetryDelayMs < 0 || options.CacheMinutes < 0)
        {
            throw new InvalidOperationException("ListenBrainz has an out-of-range numeric setting.");
        }

        var http = factory.CreateClient(ProviderName);
        http.BaseAddress = new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        http.DefaultRequestHeaders.UserAgent.Clear();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MusicHub", "1.0"));
        if (!string.IsNullOrWhiteSpace(options.Contact))
        {
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"( {options.Contact.Trim()} )"));
        }
        return new ListenBrainzProvider(http, options, cache);
    }

    public Task<IReadOnlyList<ExternalTrack>> SearchTracksAsync(
        string term, ExternalSearchFields fields, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExternalTrack>>([]);

    public Task<IReadOnlyList<ExternalArtist>> SearchArtistsAsync(string term, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExternalArtist>>([]);

    public Task<ExternalArtist?> GetArtistAsync(string externalId, CancellationToken ct) =>
        Task.FromResult<ExternalArtist?>(null);

    public Task<IReadOnlyList<ExternalTrack>> GetArtistTracksAsync(string externalId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ExternalTrack>>([]);

    public async Task<IReadOnlyList<ExternalTrackPopularity>> GetArtistPopularityAsync(
        string externalId, CancellationToken ct)
    {
        // The identifier is a MusicBrainz artist ID: anything else is not an artist ListenBrainz can know.
        if (!Guid.TryParseExact(externalId, "D", out var artistId))
        {
            return [];
        }

        var key = $"{ProviderName}:artist:{artistId:D}";
        if (cache.TryGetValue(key, out IReadOnlyList<ExternalTrackPopularity>? cached) && cached is not null)
        {
            return cached;
        }

        using var json = await client.GetJsonOrNullAsync($"popularity/top-recordings-for-artist/{artistId:D}", ct);
        var popularity = json is null ? [] : Parse(json);
        if (cacheFor > TimeSpan.Zero)
        {
            cache.Set(key, popularity, cacheFor);
        }
        return popularity;
    }

    // Entries without a recording identifier or a usable count are dropped: no count is ever invented.
    private IReadOnlyList<ExternalTrackPopularity> Parse(JsonDocument json)
    {
        if (json.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new CandidateSearchUnavailableException("ListenBrainz answered in an unexpected shape.");
        }

        var byRecording = new Dictionary<string, ExternalTrackPopularity>();
        foreach (var entry in json.RootElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !Guid.TryParseExact(Text(entry, "recording_mbid"), "D", out var recordingId)
                || Count(entry, "total_listen_count") is not { } listens)
            {
                continue;
            }
            var id = recordingId.ToString("D");
            byRecording.TryAdd(id, new ExternalTrackPopularity(Provider, id, listens, Count(entry, "total_user_count") ?? 0));
        }
        return [.. byRecording.Values];
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Count(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var count) && count >= 0
            ? count
            : null;
}

public static class ListenBrainzServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ListenBrainzProvider"/> as an <see cref="IExternalMusicSearch"/> under the key
    /// <see cref="ListenBrainzProvider.ProviderName"/> when <c>ListenBrainz:Enabled</c> is set. It is keyed so the
    /// identity provider stays the one unkeyed <see cref="IExternalMusicSearch"/>: popularity is optional enrichment.
    /// </summary>
    public static IServiceCollection AddListenBrainz(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(ListenBrainzOptions.SectionName);
        services.Configure<ListenBrainzOptions>(section);
        if (section.Get<ListenBrainzOptions>()?.Enabled != true)
        {
            return services;
        }

        services.AddMemoryCache();
        services.AddHttpClient(ListenBrainzProvider.ProviderName);
        services.AddKeyedSingleton<IExternalMusicSearch>(ListenBrainzProvider.ProviderName, (sp, _) =>
            ListenBrainzProvider.Create(
                sp.GetRequiredService<IOptions<ListenBrainzOptions>>().Value,
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IMemoryCache>()));
        return services;
    }
}
