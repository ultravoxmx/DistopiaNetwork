using DistopiaNetwork.Server.Entities;

namespace DistopiaNetwork.Server.Data.Repositories;

/// <summary>
/// Repository specializzato per le voci della cache MP3.
/// </summary>
public interface ICacheEntryRepository : IRepository<CacheEntryEntity>
{
    /// <summary>
    /// Cerca una cache entry per hash SHA-256.
    /// Ritorna null se il file non è in cache o è scaduto.
    /// </summary>
    Task<CacheEntryEntity?> GetByHashAsync(string fileHash);

    /// <summary>
    /// Ritorna tutte le entry con ExpiryTimestamp minore di DateTime.UtcNow.
    /// Usato da CacheCleanupService per la rimozione periodica.
    /// </summary>
    Task<IEnumerable<CacheEntryEntity>> GetExpiredAsync();

    /// <summary>
    /// Ritorna il numero di file attualmente in cache (non scaduti).
    /// Usato dall'endpoint /status.
    /// </summary>
    Task<int> CountActiveAsync();
}
