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
    public DbSet<ArtistExternalLink> ArtistExternalLinks => Set<ArtistExternalLink>();

    public DbSet<IdentificationOutcome> IdentificationOutcomes => Set<IdentificationOutcome>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdentificationOutcome>(outcome =>
        {
            outcome.Property(o => o.JobExecutionId).HasMaxLength(100);
            outcome.Property(o => o.FileName).HasMaxLength(IdentificationOutcome.FileNameMaxLength);
            outcome.Property(o => o.Reason).HasMaxLength(IdentificationOutcome.ReasonMaxLength);
            outcome.Property(o => o.Decision).HasConversion<string>().HasMaxLength(50);
            // A deleted file takes its diagnostics with it.
            outcome.HasOne(o => o.File).WithMany()
                .HasForeignKey(o => o.FileId).OnDelete(DeleteBehavior.Cascade);
            outcome.HasIndex(o => new { o.JobExecutionId, o.FileId });
            outcome.HasIndex(o => o.RecordedAt);
            outcome.HasIndex(o => o.FileId);
        });

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
            track.Property(t => t.Title).HasMaxLength(500);
            track.Property(t => t.Version).HasMaxLength(500);
        });

        modelBuilder.Entity<Artist>().Property(a => a.Name).HasMaxLength(500);

        modelBuilder.Entity<ArtistExternalLink>(link =>
        {
            link.Property(l => l.Source).HasMaxLength(50);
            link.Property(l => l.ExternalId).HasMaxLength(ExternalSources.MaxExternalIdLength);
            link.Property(l => l.Evidence).HasMaxLength(2000);
            link.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
            link.Property(l => l.Origin).HasConversion<string>().HasMaxLength(20);
            link.HasOne(l => l.Artist).WithMany(a => a.ExternalLinks)
                .HasForeignKey(l => l.ArtistId).OnDelete(DeleteBehavior.Cascade);
            // One row per artist and identifier (an Unlinked row is the correction), and an identifier is Linked to at
            // most one artist per source.
            link.HasIndex(l => new { l.ArtistId, l.Source, l.ExternalId }).IsUnique();
            link.HasIndex(l => new { l.Source, l.ExternalId }).IsUnique().HasFilter("\"Status\" = 'Linked'");
        });

        modelBuilder.Entity<TrackArtist>(link =>
        {
            link.Property(l => l.Role).HasMaxLength(100);
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
