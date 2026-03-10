using Microsoft.EntityFrameworkCore;
using DistopiaNetwork.Server.Entities;

namespace DistopiaNetwork.Server.Data;

/// <summary>
/// DbContext principale del nodo server.
/// Configura le tabelle SQL Server con indici ottimizzati per i pattern di accesso
/// più frequenti: streaming, sincronizzazione incrementale, cleanup cache.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<PodcastEntity> Podcasts => Set<PodcastEntity>();
    public DbSet<CacheEntryEntity> CacheEntries => Set<CacheEntryEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── Podcasts ──────────────────────────────────────────────────────────
        modelBuilder.Entity<PodcastEntity>(e =>
        {
            e.ToTable("Podcasts");
            e.HasKey(p => p.PodcastId);

            // Signature può superare 4000 char → nvarchar(MAX)
            e.Property(p => p.Signature).HasColumnType("nvarchar(MAX)");
            e.Property(p => p.PublisherPubKey).HasColumnType("nvarchar(MAX)");

            // Indice su FileHash: usato da FindByFileHashAndPublisher (rilevazione duplicati)
            e.HasIndex(p => p.FileHash)
             .HasDatabaseName("IX_Podcasts_FileHash");

            // Indice su PublishTimestamp: usato da GetSince (sincronizzazione incrementale)
            e.HasIndex(p => p.PublishTimestamp)
             .HasDatabaseName("IX_Podcasts_PublishTimestamp");

            // Indice su PublisherServer: usato da StreamingService per trovare il peer corretto
            e.HasIndex(p => p.PublisherServer)
             .HasDatabaseName("IX_Podcasts_PublisherServer");
        });

        // ── CacheEntries ──────────────────────────────────────────────────────
        modelBuilder.Entity<CacheEntryEntity>(e =>
        {
            e.ToTable("CacheEntries");
            e.HasKey(c => c.FileHash);

            // Indice su ExpiryTimestamp: usato da GetExpiredAsync (cleanup periodico)
            e.HasIndex(c => c.ExpiryTimestamp)
             .HasDatabaseName("IX_CacheEntries_Expiry");

            // FK opzionale verso Podcasts con SetNull on delete
            e.HasOne(c => c.Podcast)
             .WithMany()
             .HasForeignKey(c => c.PodcastId)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
