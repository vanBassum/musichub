using MusicHub.Api.Identification;

namespace MusicHub.Api.Acquisition;

/// <summary>How a downloaded file's identification compares with the recording that was wanted.</summary>
public enum MatchKind
{
    /// <summary>The file was identified as exactly the wanted recording.</summary>
    Same,

    /// <summary>The file was identified, as some other recording.</summary>
    Different,

    /// <summary>The identification can't say whether it is the wanted recording (no identity, or a version it can't tell apart).</summary>
    Uncertain,
}

/// <summary>
/// The one rule that decides whether an identified download is the wanted recording: the identification must name the
/// same title and the same artists. A wanted recording with a version (live, remix, ...) is never confirmed, because an
/// identification names no version and a different version must not be taken for it. A resemblance is not enough.
/// </summary>
public static class RecordingMatch
{
    public static (MatchKind Kind, string Reason) Compare(WantedRecording wanted, IdentificationResult result)
    {
        if (result.Status != IdentificationStatus.Identified || result.Identity is not { } identity)
            return (MatchKind.Uncertain, result.UnresolvedReason ?? "The file could not be identified.");

        var said = $"\"{identity.Title}\" by {string.Join(", ", identity.Artists)}";
        if (TextMatch.TitleKey(identity.Title) != TextMatch.TitleKey(wanted.Title) || !SameArtists(wanted.Artists, identity.Artists))
            return (MatchKind.Different, $"Identified as {said}, not the wanted recording.");

        return wanted.Version is { Length: > 0 } version
            ? (MatchKind.Uncertain, $"Identified as {said}, but the wanted recording is the version \"{version}\" and the identification can't confirm a version.")
            : (MatchKind.Same, $"Identified as {said}.");
    }

    private static bool SameArtists(IReadOnlyList<string> wanted, IReadOnlyList<string> identified) =>
        wanted.Select(TextMatch.Key).Where(k => k.Length > 0).ToHashSet()
            .SetEquals(identified.Select(TextMatch.Key).Where(k => k.Length > 0));
}
