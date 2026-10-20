using MusicHub.Api.Acquisition;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Tests.Acquisition;

public class AcquisitionMatchingTests
{
    private static readonly WantedRecording Song = new("Wonderwall", null, ["Oasis"]);

    private static AcquisitionCandidate Mp3(string path, int? bitrate = 320, bool? slot = true, int? queue = 0, string format = "mp3") =>
        new("slskd", "peer", path, 5_000_000, format, bitrate, HasFreeSlot: slot, QueueLength: queue);

    [Fact]
    public void Only_mp3s_that_claim_the_wanted_title_and_artist_are_considered()
    {
        var ranked = CandidateRanking.Rank(Song, [
            Mp3(@"Music\Oasis\01 Wonderwall.mp3"),
            Mp3(@"Music\Oasis\01 Wonderwall.flac", format: "flac"),
            Mp3(@"Music\Oasis\02 Champagne Supernova.mp3"),
            Mp3(@"Music\Blur\Wonderwall.mp3"),
        ]);

        Assert.Equal([@"Music\Oasis\01 Wonderwall.mp3"], ranked.Select(c => c.Path));
    }

    [Fact]
    public void The_right_recording_outranks_a_higher_bitrate_of_another_version()
    {
        var ranked = CandidateRanking.Rank(Song, [
            Mp3(@"Oasis\Wonderwall (Live at Knebworth).mp3", bitrate: 320),
            Mp3(@"Oasis\Wonderwall (Remix).mp3", bitrate: 320),
            Mp3(@"Oasis\Wonderwall.mp3", bitrate: 128),
        ]);

        Assert.Equal([@"Oasis\Wonderwall.mp3"], ranked.Select(c => c.Path));
    }

    [Fact]
    public void A_version_the_wanted_recording_carries_is_not_a_mismatch()
    {
        var live = Song with { Version = "Live" };

        var ranked = CandidateRanking.Rank(live, [Mp3(@"Oasis\Wonderwall (Live).mp3")]);

        Assert.Single(ranked);
    }

    [Fact]
    public void Among_matching_candidates_the_exact_name_comes_first_then_bitrate_then_a_free_slot()
    {
        var ranked = CandidateRanking.Rank(Song, [
            Mp3(@"Oasis\Wonderwall (Radio Mix).mp3", bitrate: 320),
            Mp3(@"Oasis\Wonderwall.mp3", bitrate: 192),
            Mp3(@"Oasis\01 - Oasis - Wonderwall.mp3", bitrate: 256, slot: false),
            Mp3(@"Oasis\03 Wonderwall.mp3", bitrate: 256, slot: true),
            Mp3(@"Oasis\Wonderwall - Oasis.mp3", bitrate: null),
        ]);

        Assert.Equal(
            [@"Oasis\03 Wonderwall.mp3", @"Oasis\01 - Oasis - Wonderwall.mp3", @"Oasis\Wonderwall.mp3", @"Oasis\Wonderwall - Oasis.mp3", @"Oasis\Wonderwall (Radio Mix).mp3"],
            ranked.Select(c => c.Path));
    }

    private static IdentificationResult Identified(string title, params string[] artists) =>
        IdentificationResult.Identified(new ProposedRecordingIdentity(title, artists, null), []);

    [Fact]
    public void An_identification_of_the_same_title_and_artists_is_the_wanted_recording()
    {
        Assert.Equal(MatchKind.Same, RecordingMatch.Compare(Song, Identified("wonderwall", "OASIS")).Kind);
        Assert.Equal(MatchKind.Same, RecordingMatch.Compare(Song, Identified("Wonderwall (feat. Someone)", "Oasis")).Kind);
    }

    [Fact]
    public void Another_title_or_other_artists_is_a_different_recording()
    {
        Assert.Equal(MatchKind.Different, RecordingMatch.Compare(Song, Identified("Wonderwall (Live)", "Oasis")).Kind);
        Assert.Equal(MatchKind.Different, RecordingMatch.Compare(Song, Identified("Wonderwall", "Mike Flowers Pops")).Kind);
    }

    [Fact]
    public void No_identity_is_uncertain_and_a_version_can_never_be_confirmed()
    {
        Assert.Equal(MatchKind.Uncertain, RecordingMatch.Compare(Song, IdentificationResult.Unresolved("no match")).Kind);
        Assert.Equal(MatchKind.Uncertain,
            RecordingMatch.Compare(Song with { Version = "Live" }, Identified("Wonderwall", "Oasis")).Kind);
    }
}
