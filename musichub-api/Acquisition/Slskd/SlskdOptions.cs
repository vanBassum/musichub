namespace MusicHub.Api.Acquisition.Slskd;

/// <summary>
/// Binds the <c>Slskd</c> configuration section (environment variables <c>Slskd__*</c>). The provider is registered
/// only when <see cref="BaseUrl"/> is set.
/// </summary>
public sealed class SlskdOptions
{
    public const string SectionName = "Slskd";

    /// <summary>The slskd HTTP API, for example <c>http://slskd:5030</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>An slskd API key (secret, never logged), sent in the <c>X-API-Key</c> header.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// slskd's downloads folder as MusicHub sees it (a mount shared with the slskd container). slskd's API doesn't
    /// serve the file, so without it a download cannot be handed to ingestion.
    /// </summary>
    public string? DownloadsPath { get; set; }

    /// <summary>How long a search may collect answers.</summary>
    public int SearchTimeoutSeconds { get; set; } = 15;

    /// <summary>The most candidates a search returns.</summary>
    public int MaxCandidates { get; set; } = 50;

    /// <summary>How long each request to slskd may take.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>How long a download may take, from queueing it until it has finished.</summary>
    public int DownloadTimeoutSeconds { get; set; } = 600;

    /// <summary>How often a running search or download is checked.</summary>
    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>How often a request that slskd answered with 429 is repeated before the 429 is reported.</summary>
    public int RateLimitMaxRetries { get; set; } = 4;

    /// <summary>The first wait after a 429 without <c>Retry-After</c>; it doubles on each retry.</summary>
    public int RateLimitBaseDelayMs { get; set; } = 1000;

    /// <summary>The longest wait between two attempts, also when <c>Retry-After</c> asks for more.</summary>
    public int RateLimitMaxDelayMs { get; set; } = 30000;

    /// <summary>Throws, naming the key, when the options cannot work, so a bad setup is loud.</summary>
    public void Validate()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Slskd__BaseUrl must be an absolute http(s) URL.");
        }
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Slskd is not configured: Slskd__ApiKey is required when Slskd__BaseUrl is set.");
        }
        if (SearchTimeoutSeconds < 1 || MaxCandidates < 1 || RequestTimeoutSeconds < 1
            || DownloadTimeoutSeconds < 1 || PollIntervalMs < 1
            || RateLimitMaxRetries < 0 || RateLimitBaseDelayMs < 0 || RateLimitMaxDelayMs < 0)
        {
            throw new InvalidOperationException("Slskd has an out-of-range numeric setting.");
        }
    }
}
