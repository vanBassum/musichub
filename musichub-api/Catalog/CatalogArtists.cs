using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;

namespace MusicHub.Api.Catalog;

/// <summary>
/// The one definition of artist identity for catalog creation: names are trimmed and distinct ignoring case, and a
/// name that matches an existing artist (ignoring case, lowest id) reuses it. Used under
/// <see cref="CatalogReconciliation.CatalogLock"/> by every path that creates catalog tracks.
/// </summary>
internal static class CatalogArtists
{
    public static List<string> Normalize(IEnumerable<string?> names) => names
        .Select(n => n?.Trim() ?? "")
        .Where(n => n.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    public static async Task AttachAsync(
        MusicHubDbContext db, Track track, IEnumerable<string> names, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var name in names)
        {
            var lower = name.ToLower();
            var artist = await db.Artists.OrderBy(a => a.Id)
                             .FirstOrDefaultAsync(a => a.Name.ToLower() == lower, cancellationToken)
                         ?? new Artist { Name = name, CreatedAt = now };
            track.Artists.Add(new TrackArtist { Artist = artist });
        }
    }
}
