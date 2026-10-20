using Microsoft.EntityFrameworkCore;
using MusicHub.Api.Acquisition;
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
    public DbSet<ArtistExternalLink> ArtistExternalLinks => Set<ArtistExternalLink>();

    public DbSet<IdentificationOutcome> IdentificationOutcomes => Set<IdentificationOutcome>();

    public DbSet<AppliedIdentification> AppliedIdentifications => Set<AppliedIdentification>();

    public DbSet<AcquisitionDownload> AcquisitionDownloads => Set<AcquisitionDownload>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AcquisitionDownload>(download =>
        {
            download.Property(d => d.Provider).HasMaxLength(AcquisitionDownload.ProviderMaxLength);
            download.Property(d => d.Source).HasMaxLength(AcquisitionDownload.SourceMaxLength);
            download.Property(d => d.RemotePath).HasMaxLength(AcquisitionDownload.RemotePathMaxLength);
            download.Property(d => d.Reason).HasMaxLength(AcquisitionDownload.ReasonMaxLength);
            download.Property(d => d.ContentHash).HasMaxLength(64);
            download.Property(d => d.AudioHash).HasMaxLength(64);
            download.Property(d => d.AudioHashStatus).HasConversion<string>().HasMaxLength(20);
            download.Property(d => d.State).HasConversion<string>().HasMaxLength(20);
            download.Property(d => d.Failure).HasConversion<string>().HasMaxLength(30);
            download.Property(d => d.Outcome).HasConversion<string>().HasMaxLength(20);
            // History outlives its track and its file: deleting either only empties the reference.
            download.HasOne(d => d.IntendedTrack).WithMany()
                .HasForeignKey(d => d.IntendedTrackId).OnDelete(DeleteBehavior.SetNull);
            download.HasOne(d => d.File).WithMany()
                .HasForeignKey(d => d.FileId).OnDelete(DeleteBehavior.SetNull);
            download.HasIndex(d => new { d.Provider, d.Source, d.RemotePath });
            download.HasIndex(d => d.ContentHash);
            download.HasIndex(d => d.AudioHash);
            download.HasIndex(d => d.StartedAt);
        });

        modelBuilder.Entity<AppliedIdentification>(applied =>
        {
            applied.Property(a => a.Source).HasConversion<string>().HasMaxLength(50);
            // A deleted file takes its history with it; a deleted track or attempt only empties the reference.
            applied.HasOne(a => a.File).WithMany()
                .HasForeignKey(a => a.FileId).OnDelete(DeleteBehavior.Cascade);
            applied.HasOne(a => a.Track).WithMany()
                .HasForeignKey(a => a.TrackId).OnDelete(DeleteBehavior.SetNull);
            applied.HasOne(a => a.Attempt).WithMany()
                .HasForeignKey(a => a.AttemptId).OnDelete(DeleteBehavior.SetNull);
            applied.HasIndex(a => new { a.FileId, a.AppliedAt });
            applied.HasIndex(a => a.TrackId);
        });

        modelBuilder.Entity<IdentificationOutcome>(outcome =>
        {
            outcome.Property(o => o.JobExecutionId).HasMaxLength(100);
            outcome.Property(o => o.FileName).HasMaxLength(IdentificationOutcome.FileNameMaxLength);
            outcome.Property(o => o.Reason).HasMaxLength(IdentificationOutcome.ReasonMaxLength);
            outcome.Property(o => o.Decision).HasConversion<string>().HasMaxLength(50);
            // A deleted file takes its attempts with it.
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

        modelBuilder.Entity<Artist>().Property(a => a.Name).HasMaxLength(CatalogLimits.ArtistName);

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
