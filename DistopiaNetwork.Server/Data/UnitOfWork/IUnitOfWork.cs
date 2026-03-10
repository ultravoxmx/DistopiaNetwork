using DistopiaNetwork.Server.Data.Repositories;

namespace DistopiaNetwork.Server.Data.UnitOfWork;

/// <summary>
/// Unit of Work: coordina più repository in un'unica transazione atomica.
/// Un solo SaveChangesAsync() = una sola transazione SQL che include
/// tutte le operazioni fatte tramite Podcasts e CacheEntries.
/// 
/// Esempio di uso transazionale in un controller o service:
/// <code>
/// await _uow.Podcasts.AddAsync(podcastEntity);
/// await _uow.CacheEntries.AddAsync(cacheEntity);
/// await _uow.SaveChangesAsync(); // entrambe le scritture in un'unica transazione
/// </code>
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IPodcastRepository Podcasts { get; }
    ICacheEntryRepository CacheEntries { get; }

    /// <summary>
    /// Persiste tutte le modifiche pendenti in un'unica transazione.
    /// Ritorna il numero di righe effettivamente scritte.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
