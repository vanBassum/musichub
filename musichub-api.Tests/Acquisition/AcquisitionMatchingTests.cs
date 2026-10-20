using System.Security.Cryptography;
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

    private static async Task<(long Start, long End)> RangeAsync(byte[] bytes) =>
        await AudioHashing.PayloadRangeAsync(new MemoryStream(bytes), CancellationToken.None);

    private static byte[] Id3v2(int size, bool footer = false)
    {
        var tag = new byte[10 + size + (footer ? 10 : 0)];
        "ID3"u8.CopyTo(tag);
        tag[3] = 4;
        tag[5] = (byte)(footer ? 0x10 : 0);
        tag[8] = (byte)(size >> 7 & 0x7f);
        tag[9] = (byte)(size & 0x7f);
        return tag;
    }

    private static byte[] Id3v1()
    {
        var tag = new byte[128];
        "TAG"u8.CopyTo(tag);
        return tag;
    }

    [Fact]
    public async Task The_payload_excludes_id3v2_and_id3v1_tags()
    {
        var audio = Enumerable.Range(0, 1000).Select(i => (byte)(i % 251 + 1)).ToArray();
        var file = Id3v2(300).Concat(audio).Concat(Id3v1()).ToArray();

        Assert.Equal((310L, 1310L), await RangeAsync(file));
    }

    [Fact]
    public async Task The_payload_excludes_an_id3v2_footer_and_an_ape_tag()
    {
        var audio = Enumerable.Repeat((byte)0xFF, 500).ToArray();
        var ape = new byte[32 + 40];
        "APETAGEX"u8.CopyTo(ape.AsSpan(40));
        BitConverter.GetBytes(72u - 32u).CopyTo(ape, 40 + 12); // size of the items and the footer
        BitConverter.GetBytes(0x80000000u).CopyTo(ape, 40 + 20); // flags: a header precedes
        var file = Id3v2(20, footer: true).Concat(audio).Concat(ape).ToArray();

        Assert.Equal((40L, 540L), await RangeAsync(file));
    }

    [Fact]
    public async Task Changed_tags_change_the_file_hash_but_not_the_audio_hash()
    {
        var audio = Enumerable.Range(0, 2000).Select(i => (byte)(i * 7 % 255)).ToArray();
        var tagged = Id3v2(100).Concat(audio).Concat(Id3v1()).ToArray();
        var retagged = Id3v2(400).Concat(audio).ToArray();

        var a = await AudioHashing.ComputeAsync(new MemoryStream(tagged), CancellationToken.None);
        var b = await AudioHashing.ComputeAsync(new MemoryStream(retagged), CancellationToken.None);

        Assert.NotEqual(a.FileSha256, b.FileSha256);
        Assert.Equal(a.AudioSha256, b.AudioSha256);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(audio)), a.AudioSha256);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(tagged)), a.FileSha256);
        Assert.Equal(AudioHashing.Mp3PayloadAlgorithm, a.AudioHashAlgorithm);
        Assert.Equal(tagged.Length, a.SizeBytes);
    }

    [Fact]
    public async Task A_file_that_is_all_tag_has_no_audio_hash()
    {
        var hashes = await AudioHashing.ComputeAsync(new MemoryStream(Id3v2(50)), CancellationToken.None);

        Assert.Null(hashes.AudioSha256);
        Assert.NotEmpty(hashes.FileSha256);
    }
}
