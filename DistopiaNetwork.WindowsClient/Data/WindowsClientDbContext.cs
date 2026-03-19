using Microsoft.EntityFrameworkCore;

namespace DistopiaNetwork.WindowsClient.Data;

public class WindowsClientDbContext : DbContext
{
    public WindowsClientDbContext(DbContextOptions<WindowsClientDbContext> options)
        : base(options)
    {
    }

    public DbSet<LocalEpisodeEntity> Episodes => Set<LocalEpisodeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalEpisodeEntity>(e =>
        {
            e.ToTable("Episodes");
            e.HasKey(x => x.PodcastId);
            e.HasIndex(x => x.FileHash).IsUnique();
            e.HasIndex(x => x.CreatedAt);
        });
    }
}
