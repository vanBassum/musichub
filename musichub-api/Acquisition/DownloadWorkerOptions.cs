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

    /// <summary>Throws, naming the key, when a setting cannot work.</summary>
    public void Validate()
    {
        if (MaxConcurrentDownloads < 1 || PollIntervalSeconds < 1 || MaxAttempts < 1 || RetryDelaySeconds < 0)
            throw new InvalidOperationException("DownloadWorker has an out-of-range numeric setting.");
    }
}
