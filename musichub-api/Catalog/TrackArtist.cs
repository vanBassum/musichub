namespace MusicHub.Api.Catalog;

/// <summary>
/// Many-to-many link between a track and an artist, with an optional free-text role
/// (e.g. "Main", "Featuring", "Remixer"). A track/artist pair is unique per role.
/// </summary>
public class TrackArtist
{
    public int Id { get; set; }
    public int TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public int ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
    public string? Role { get; set; }
}
