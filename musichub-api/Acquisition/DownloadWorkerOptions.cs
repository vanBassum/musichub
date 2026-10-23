namespace MusicHub.Api.Acquisition;

/// <summary>Binds the <c>DownloadWorker</c> configuration section (environment variables <c>DownloadWorker__*</c>).</summary>
public sealed class DownloadWorkerOptions
{
    public const string SectionName = "DownloadWorker";

    /// <summary>How many queued downloads are processed at the same time.</summary>
    public int MaxConcurrentDownloads { get; set; } = 2;

    /// <summary>How often an idle worker looks for a request it may claim.</summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>How many times a request is claimed before a failed attempt is final.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>The wait before the second attempt; each further attempt waits that much longer again.</summary>
    public int RetryDelaySeconds { get; set; } = 60;

    /// <summary>The wait after failed attempt <paramref name="attemptNumber"/>, or null when no attempts are left.</summary>
    public TimeSpan? RetryWait(int attemptNumber) =>
        attemptNumber >= MaxAttempts ? null : TimeSpan.FromSeconds(RetryDelaySeconds * attemptNumber);

    /// <summary>How long a source that was confirmed offline is left out of every search result.</summary>
    public int SourceOfflineCooldownMinutes { get; set; } = 60;

    /// <summary>The wait before the first fresh search for a track that ran out of candidates; it doubles each time.</summary>
    public int ResearchBaseDelayMinutes { get; set; } = 15;

    /// <summary>The longest wait between two fresh searches for one track.</summary>
    public int ResearchMaxDelayMinutes { get; set; } = 720;

    /// <summary>How many scheduled fresh searches a track gets before it is left to a manual search.</summary>
    public int ResearchMaxAttempts { get; set; } = 6;

    /// <summary>Throws, naming the key, when a setting cannot work.</summary>
    public void Validate()
    {
        if (SourceOfflineCooldownMinutes < 0 || ResearchBaseDelayMinutes < 1 || ResearchMaxDelayMinutes < ResearchBaseDelayMinutes
            || ResearchMaxAttempts < 1)
            throw new InvalidOperationException("DownloadWorker has an out-of-range research or cooldown setting.");
        if (MaxConcurrentDownloads < 1 || PollIntervalSeconds < 1 || MaxAttempts < 1 || RetryDelaySeconds < 0)
            throw new InvalidOperationException("DownloadWorker has an out-of-range numeric setting.");
    }
}
