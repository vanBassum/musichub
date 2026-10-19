using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Catalog;
using MusicHub.Api.Ingestion;

namespace MusicHub.Api.Data;

public class MusicHubDbContext(DbContextOptions<MusicHubDbContext> options) : DbContext(options)
{
    public DbSet<FileRecord> Files => Set<FileRecord>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<TrackArtist> TrackArtists => Set<TrackArtist>();
    public DbSet<TrackExternalId> TrackExternalIds => Set<TrackExternalId>();
    public DbSet<IdentityReview> IdentityReviews => Set<IdentityReview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileRecord>(file =>
        {
            file.Property(f => f.Status).HasConversion<string>();
            file.Property(f => f.FailureReason).HasConversion<string>();
            // Files outlive tracks: deleting a track leaves its files unassociated.
            file.HasOne(f => f.Track).WithMany(t => t.Files)
                .HasForeignKey(f => f.TrackId).OnDelete(DeleteBehavior.SetNull);
            file.HasIndex(f => f.TrackId);
        });

        modelBuilder.Entity<Track>(track =>
        {
            track.Property(t => t.Title).HasMaxLength(CatalogLimits.Title);
            track.Property(t => t.Version).HasMaxLength(CatalogLimits.Version);
        });

        modelBuilder.Entity<TrackExternalId>(external =>
        {
            external.Property(e => e.Provider).HasMaxLength(CatalogLimits.Provider);
            external.Property(e => e.ExternalId).HasMaxLength(CatalogLimits.ExternalId);
            external.HasOne(e => e.Track).WithMany(t => t.ExternalIds)
                .HasForeignKey(e => e.TrackId).OnDelete(DeleteBehavior.Cascade);
            // A provider recording is one catalog track.
            external.HasIndex(e => new { e.Provider, e.ExternalId }).IsUnique();
            external.HasIndex(e => e.TrackId);
        });

        modelBuilder.Entity<IdentityReview>(review =>
        {
            review.Property(r => r.Provider).HasMaxLength(CatalogLimits.Provider);
            review.Property(r => r.ExternalId).HasMaxLength(CatalogLimits.ExternalId);
            review.Property(r => r.Title).HasMaxLength(CatalogLimits.Title);
            review.Property(r => r.Disambiguation).HasMaxLength(CatalogLimits.Version);
            review.HasIndex(r => new { r.Provider, r.ExternalId }).IsUnique();
        });

        modelBuilder.Entity<Artist>().Property(a => a.Name).HasMaxLength(CatalogLimits.ArtistName);

        modelBuilder.Entity<TrackArtist>(link =>
        {
            link.Property(l => l.Role).HasMaxLength(CatalogLimits.Role);
            link.HasOne(l => l.Track).WithMany(t => t.Artists)
                .HasForeignKey(l => l.TrackId).OnDelete(DeleteBehavior.Cascade);
            link.HasOne(l => l.Artist).WithMany(a => a.Tracks)
                .HasForeignKey(l => l.ArtistId).OnDelete(DeleteBehavior.Restrict);
            // NULLs are distinct in a unique index, so a role-less link needs its own filtered index.
            link.HasIndex(l => new { l.TrackId, l.ArtistId, l.Role }).IsUnique();
            link.HasIndex(l => new { l.TrackId, l.ArtistId }).IsUnique().HasFilter("\"Role\" IS NULL");
            link.HasIndex(l => l.ArtistId);
        });
    }
}
