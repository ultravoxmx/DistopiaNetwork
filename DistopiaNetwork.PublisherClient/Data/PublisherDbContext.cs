using DistopiaNetwork.PublisherClient.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistopiaNetwork.PublisherClient.Data;

/// <summary>
/// DbContext SQLite per il publisher client.
/// Il file .db viene creato in %LOCALAPPDATA%/DistopiaNetwork/publisher.db
/// (o ~/.local/share/DistopiaNetwork/publisher.db su Linux/macOS).
/// Zero configurazione: SQLite crea il file automaticamente alla prima migrazione.
/// </summary>
public class PublisherDbContext : DbContext
{
    public PublisherDbContext(DbContextOptions<PublisherDbContext> options)
        : base(options) { }

    public DbSet<LocalEpisodeEntity> Episodes => Set<LocalEpisodeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalEpisodeEntity>(e =>
        {
            e.ToTable("Episodes");
            e.HasKey(ep => ep.PodcastId);

            // NOTA: niente HasDatabaseName() — in SQLite 9.x causa una migrazione
            // "rebuild" che fallisce su database vuoto. EF gestisce i nomi automaticamente.
            e.HasIndex(ep => ep.FileHash).IsUnique();
            e.HasIndex(ep => ep.UploadStatus);
            e.HasIndex(ep => ep.CreatedAt);

            e.Property(ep => ep.UploadStatus)
             .HasDefaultValue(UploadStatuses.Draft);
        });
    }
}