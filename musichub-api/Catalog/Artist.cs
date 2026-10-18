namespace MusicHub.Api.Catalog;

/// <summary>An artist, independent of any track; related to tracks through <see cref="TrackArtist"/>.</summary>
public class Artist
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<TrackArtist> Tracks { get; set; } = [];
}
