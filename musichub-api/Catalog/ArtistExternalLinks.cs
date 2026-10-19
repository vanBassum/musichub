using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Data;
using MusicHub.Api.Identification;

namespace MusicHub.Api.Catalog;

/// <summary>Whether an external identifier may be linked to an artist.</summary>
public enum LinkCheck
{
    /// <summary>Nothing stands in the way.</summary>
    Free,

    /// <summary>The artist already has this identifier linked.</summary>
    AlreadyLinked,

    /// <summary>The identifier is linked to another artist.</summary>
    TakenByOtherArtist,

    /// <summary>A person unlinked this identifier from this artist.</summary>
    Unlinked,

    /// <summary>The source is unknown or the identifier is not valid for it.</summary>
    Invalid,
}

/// <summary>
/// The rules for <see cref="ArtistExternalLink"/>s, shared by the automatic reconciliation and the correction API.
/// Automation only ever adds a link where nothing stands in the way: it never moves, changes or re-adds a link that a
/// person decided on. A person's decisions are the only way a link is changed after it exists.
/// </summary>
public sealed class ArtistExternalLinks(MusicHubDbContext db, TimeProvider time)
{
    /// <summary>The artist an identifier is linked to, or null when it is linked to none.</summary>
    public async Task<Artist?> FindLinkedArtistAsync(ArtistReference reference, CancellationToken ct)
    {
        if (ExternalSources.Normalize(reference.Source, reference.ExternalId) is not { } normalized)
            return null;
        var (source, id) = normalized;
        return await db.ArtistExternalLinks
            .Where(l => l.Source == source && l.ExternalId == id && l.Status == ExternalLinkStatus.Linked)
            .Select(l => l.Artist)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>What stands in the way of linking <paramref name="reference"/> to <paramref name="artist"/> (not yet saved is fine).</summary>
    public async Task<LinkCheck> CheckAsync(Artist artist, ArtistReference reference, CancellationToken ct)
    {
        if (ExternalSources.Normalize(reference.Source, reference.ExternalId) is not { } normalized)
            return LinkCheck.Invalid;
        var (source, id) = normalized;
        // A link added in this unit of work and not saved yet counts too.
        var pending = db.ArtistExternalLinks.Local.FirstOrDefault(l => l.Id == 0 && l.Source == source && l.ExternalId == id);
        if (pending is not null)
            return ReferenceEquals(pending.Artist, artist) ? LinkCheck.AlreadyLinked : LinkCheck.TakenByOtherArtist;
        return await CheckAsync(artist.Id, source, id, ct);
    }

    private async Task<LinkCheck> CheckAsync(int artistId, string source, string id, CancellationToken ct)
    {
        var rows = await db.ArtistExternalLinks.AsNoTracking()
            .Where(l => l.Source == source && l.ExternalId == id)
            .ToListAsync(ct);
        var linked = rows.FirstOrDefault(l => l.Status == ExternalLinkStatus.Linked);
        if (linked is not null)
            return linked.ArtistId == artistId ? LinkCheck.AlreadyLinked : LinkCheck.TakenByOtherArtist;
        if (rows.Any(l => l.ArtistId == artistId))
            return LinkCheck.Unlinked;
        return LinkCheck.Free;
    }

    /// <summary>
    /// Adds an automatic link, to be saved by the caller, when <see cref="CheckAsync(Artist, ArtistReference, CancellationToken)"/>
    /// says <see cref="LinkCheck.Free"/>; returns what it found otherwise, adding nothing.
    /// </summary>
    public async Task<LinkCheck> TryLinkAutomaticallyAsync(
        Artist artist, ArtistReference reference, string evidence, CancellationToken ct)
    {
        var check = await CheckAsync(artist, reference, ct);
        if (check != LinkCheck.Free)
            return check;

        var (source, id) = ExternalSources.Normalize(reference.Source, reference.ExternalId)!.Value;
        var now = time.GetUtcNow();
        db.ArtistExternalLinks.Add(new ArtistExternalLink
        {
            Artist = artist,
            Source = source,
            ExternalId = id,
            Status = ExternalLinkStatus.Linked,
            Origin = ExternalLinkOrigin.Automatic,
            Evidence = evidence,
            CreatedAt = now,
            UpdatedAt = now,
        });
        return LinkCheck.Free;
    }

    /// <summary>A person links an identifier to an artist. A link they unlinked before is linked again.</summary>
    public async Task<(LinkCheck Check, ArtistExternalLink? Link)> LinkManuallyAsync(
        int artistId, string source, string id, CancellationToken ct)
    {
        var check = await CheckAsync(artistId, source, id, ct);
        if (check == LinkCheck.TakenByOtherArtist)
            return (check, null);

        var now = time.GetUtcNow();
        var link = await db.ArtistExternalLinks.FirstOrDefaultAsync(
            l => l.ArtistId == artistId && l.Source == source && l.ExternalId == id, ct);
        if (link is null)
        {
            link = new ArtistExternalLink
            {
                ArtistId = artistId, Source = source, ExternalId = id, CreatedAt = now,
            };
            db.ArtistExternalLinks.Add(link);
        }
        if (link.Status != ExternalLinkStatus.Linked || link.Origin != ExternalLinkOrigin.Manual)
        {
            link.Status = ExternalLinkStatus.Linked;
            link.Origin = ExternalLinkOrigin.Manual;
            link.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return (check, link);
    }

    /// <summary>A person unlinks an identifier; the row stays as an Unlinked correction. Null when the artist has no such link.</summary>
    public async Task<ArtistExternalLink?> UnlinkAsync(int artistId, int linkId, CancellationToken ct)
    {
        var link = await db.ArtistExternalLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.ArtistId == artistId, ct);
        if (link is null || link.Status == ExternalLinkStatus.Unlinked)
            return link;

        link.Status = ExternalLinkStatus.Unlinked;
        link.Origin = ExternalLinkOrigin.Manual;
        link.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return link;
    }
}
