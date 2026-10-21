namespace MusicHub.Api.Acquisition;

/// <summary>Binds the <c>MissingMusic</c> configuration section (environment variables <c>MissingMusic__*</c>).</summary>
public sealed class MissingMusicOptions
{
    public const string SectionName = "MissingMusic";

    /// <summary>How many tracks are searched at the same time.</summary>
    public int MaxConcurrentTracks { get; set; } = 2;

    /// <summary>Throws, naming the key, when a setting cannot work.</summary>
    public void Validate()
    {
        if (MaxConcurrentTracks < 1)
            throw new InvalidOperationException("MissingMusic has an out-of-range numeric setting.");
    }
}
