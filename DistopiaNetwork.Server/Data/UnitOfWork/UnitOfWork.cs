using DistopiaNetwork.Server.Data.Repositories;

namespace DistopiaNetwork.Server.Data.UnitOfWork;

/// <summary>
/// Implementazione concreta di IUnitOfWork.
/// Tutti i repository condividono lo stesso DbContext, garantendo
/// che le loro operazioni siano parte della medesima transazione.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public IPodcastRepository Podcasts { get; }
    public ICacheEntryRepository CacheEntries { get; }

    public UnitOfWork(
        AppDbContext db,
        IPodcastRepository podcasts,
        ICacheEntryRepository cacheEntries)
    {
        _db = db;
        Podcasts = podcasts;
        CacheEntries = cacheEntries;
    }

    /// <summary>
    /// Delega a DbContext.SaveChangesAsync — una sola chiamata SQL.
    /// EF Core wrappa automaticamente le operazioni in una transazione implicita.
    /// </summary>
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);

    public async ValueTask DisposeAsync()
        => await _db.DisposeAsync();
}
