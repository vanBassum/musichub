namespace MusicHub.Api.Identification;

/// <summary>
/// The version of the identification methods an attempt ran with: the build's commit (<c>APP_COMMIT</c>, set in the
/// image), or <c>dev</c> outside one. The methods are not versioned one by one; a new build is the unit that may
/// identify differently, so it is what an attempt records.
/// </summary>
public static class BuildVersion
{
    public static string Current { get; } =
        Environment.GetEnvironmentVariable("APP_COMMIT") is { Length: > 0 } commit && commit != "unknown" ? commit : "dev";
}
