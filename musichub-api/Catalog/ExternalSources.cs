using System.Text.RegularExpressions;

namespace MusicHub.Api.Catalog;

/// <summary>How a source (provider) and its artist identifiers are written, and where they lead.</summary>
public static partial class ExternalSources
{
    public const string MusicBrainz = "musicbrainz";
    public const int MaxExternalIdLength = 100;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,49}$")]
    private static partial Regex SourceName();

    [GeneratedRegex(@"^\S{1,100}$")]
    private static partial Regex PlainId();

    /// <summary>
    /// The normalized source (lower case) and identifier, or null when either is not valid. A MusicBrainz artist ID
    /// is a UUID, written lower case; another source's identifier is kept as given, without whitespace.
    /// </summary>
    public static (string Source, string ExternalId)? Normalize(string? source, string? externalId)
    {
        var s = source?.Trim().ToLowerInvariant();
        var id = externalId?.Trim();
        if (s is null || id is null || !SourceName().IsMatch(s))
            return null;
        if (s == MusicBrainz)
            return Guid.TryParseExact(id, "D", out var guid) ? (s, guid.ToString("D")) : null;
        return PlainId().IsMatch(id) ? (s, id) : null;
    }

    public static string ArtistUrl(string source, string externalId) =>
        source == MusicBrainz ? $"https://musicbrainz.org/artist/{externalId}" : "";
}
