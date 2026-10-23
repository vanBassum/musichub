using MusicHub.Api.ExternalSearch;

namespace MusicHub.Api.Identification;

public enum IdentificationStrategy
{
    RuleBased,
    Ai,
}

/// <summary>Binds the <c>Identification</c> configuration section (environment variables <c>Identification__*</c>).</summary>
public sealed class IdentificationOptions
{
    public const string SectionName = "Identification";

    /// <summary>Which resolver is used. Defaults to the rule-based one, which needs no AI provider.</summary>
    public IdentificationStrategy Strategy { get; set; } = IdentificationStrategy.RuleBased;

    /// <summary>The AI model to refer to; required when <see cref="Strategy"/> is Ai.</summary>
    public string? AiModel { get; set; }

    /// <summary>How many evidence tools the AI may run for one file before it has to propose or give up.</summary>
    public int AiMaxSteps { get; set; } = 5;

    /// <summary>The MusicBrainz candidate search; off unless enabled.</summary>
    public MusicBrainzOptions MusicBrainz { get; set; } = new();
}

/// <summary>Binds <c>Identification:MusicBrainz</c> (environment variables <c>Identification__MusicBrainz__*</c>).</summary>
public sealed class MusicBrainzOptions : IRateLimitedJsonOptions
{
    public bool Enabled { get; set; }

    /// <summary>The web service root. Defaults to the public MusicBrainz service.</summary>
    public string BaseUrl { get; set; } = "https://musicbrainz.org/ws/2/";

    /// <summary>
    /// How MusicBrainz can reach whoever runs this (an email address or URL); required when enabled, because its
    /// usage policy asks for a User-Agent with contact information.
    /// </summary>
    public string? Contact { get; set; }

    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>The least time between two requests; MusicBrainz allows about one request per second.</summary>
    public int MinRequestIntervalMs { get; set; } = 1100;

    /// <summary>How often a transient failure (timeout, 429, 5xx, network) is retried.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>The wait before a retry, times the attempt number; a Retry-After header wins, up to <see cref="MaxRetryDelayMs"/>.</summary>
    public int RetryDelayMs { get; set; } = 2000;

    public int MaxRetryDelayMs { get; set; } = 10000;

    /// <summary>How many recordings one search asks for.</summary>
    public int MaxRecordings { get; set; } = 5;

    /// <summary>How many releases of one recording become candidates.</summary>
    public int MaxReleasesPerRecording { get; set; } = 3;
}
