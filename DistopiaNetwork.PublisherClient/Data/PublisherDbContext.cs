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

            // FileHash UNIQUE: non è possibile pubblicare due volte lo stesso file
            e.HasIndex(ep => ep.FileHash)
             .IsUnique()
             .HasDatabaseName("UX_Episodes_FileHash");

            // UploadStatus: per GetPendingAsync() e GetAllByStatusAsync()
            e.HasIndex(ep => ep.UploadStatus)
             .HasDatabaseName("IX_Episodes_UploadStatus");

            // CreatedAt: per l'ordinamento cronologico nella lista episodi
            e.HasIndex(ep => ep.CreatedAt)
             .HasDatabaseName("IX_Episodes_CreatedAt");

            e.Property(ep => ep.UploadStatus)
             .HasDefaultValue(UploadStatuses.Draft);
        });
    }
}
