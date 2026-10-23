using MusicHub.Api.Catalog;

namespace MusicHub.Api.Acquisition;

/// <summary>The recording a search is for, as the catalog track names it.</summary>
public sealed record WantedRecording(string Title, string? Version, IReadOnlyList<string> Artists)
{
    /// <summary>The recording a track names; its artists (loaded with it) in the order they were credited.</summary>
    public static WantedRecording Of(Track track) => new(
        track.Title, track.Version, track.Artists.OrderBy(a => a.Id).Select(a => a.Artist.Name).ToList());
}

/// <summary>
/// Orders what a provider offers. Which recording a candidate is comes first: only MP3s whose name claims the wanted
/// title and artist, with no marker of another version (live, remix, ...) that the wanted recording doesn't carry, are
/// considered, and those whose file name is exactly the title come before the rest. Within that, the better MP3 comes
/// first (bitrate, then a free slot, then a short queue). A claim is only a guess: identification decides after the
/// download, and the order only says what to try first. A lower bitrate is never excluded, only tried later.
/// </summary>
public static class CandidateRanking
{
    // Words that name another version of a recording. Whole words, in the path of the candidate.
    private static readonly HashSet<string> VersionMarkers =
    [
        "live", "remix", "remixed", "rmx", "acoustic", "instrumental", "karaoke", "cover", "demo", "unplugged",
        "tribute", "remaster", "remastered", "edit", "extended", "version", "medley", "mashup", "bootleg", "session",
        "sessions", "rework", "reprise",
    ];

    public static IReadOnlyList<AcquisitionCandidate> Rank(WantedRecording wanted, IEnumerable<AcquisitionCandidate> offered)
    {
        var titleWords = TextMatch.Words(wanted.Title).ToHashSet();
        var artistWords = wanted.Artists.Select(a => TextMatch.Words(a).ToHashSet()).Where(w => w.Count > 0).ToList();
        var ownWords = titleWords.Union(TextMatch.Words(wanted.Version ?? "")).ToHashSet();

        return offered
            .Select(c => (Candidate: c, Words: TextMatch.Words(c.Path), Name: TextMatch.Words(NameWithoutExtension(c))))
            .Where(x => IsMp3(x.Candidate)
                        && titleWords.IsSubsetOf(x.Name)
                        && artistWords.Any(a => a.IsSubsetOf(x.Words))
                        && !x.Words.Any(w => VersionMarkers.Contains(w) && !ownWords.Contains(w)))
            .OrderByDescending(x => IsExactName(x.Name, titleWords, artistWords))
            .ThenByDescending(x => x.Candidate.BitrateKbps ?? 0)
            .ThenByDescending(x => x.Candidate.HasFreeSlot == true)
            .ThenBy(x => x.Candidate.QueueLength ?? int.MaxValue)
            .ThenBy(x => x.Candidate.Source, StringComparer.Ordinal)
            .ThenBy(x => x.Candidate.Path, StringComparer.Ordinal)
            .Select(x => x.Candidate)
            .ToList();
    }

    private static bool IsMp3(AcquisitionCandidate c) =>
        string.Equals(c.Format, "mp3", StringComparison.OrdinalIgnoreCase);

    private static string NameWithoutExtension(AcquisitionCandidate c) => Path.GetFileNameWithoutExtension(c.FileName);

    // The file name is the title once a track number and the artist's name are set aside ("01 - Artist - Title").
    private static bool IsExactName(IReadOnlyList<string> name, HashSet<string> titleWords, List<HashSet<string>> artistWords) =>
        name.Where(w => !w.All(char.IsDigit) && !artistWords.Any(a => a.Contains(w)))
            .ToHashSet()
            .SetEquals(titleWords.Where(w => !artistWords.Any(a => a.Contains(w))));
}
