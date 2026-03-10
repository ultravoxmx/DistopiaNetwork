using Microsoft.EntityFrameworkCore;
using DistopiaNetwork.Server.Entities;

namespace DistopiaNetwork.Server.Data.Repositories;

/// <summary>
/// Implementazione concreta di ICacheEntryRepository.
/// Gestisce la persistenza delle voci di cache in SQL Server,
/// sincronizzando lo stato del DB con i file fisici su disco.
/// </summary>
public class CacheEntryRepository : ICacheEntryRepository
{
    private readonly AppDbContext _db;

    public CacheEntryRepository(AppDbContext db) => _db = db;

    public async Task<CacheEntryEntity?> GetByIdAsync(string id)
        => await _db.CacheEntries.FindAsync(id);

    public async Task<CacheEntryEntity?> GetByHashAsync(string fileHash)
        => await _db.CacheEntries.FindAsync(fileHash);

    public async Task<IEnumerable<CacheEntryEntity>> GetAllAsync()
        => await _db.CacheEntries
            .AsNoTracking()
            .OrderByDescending(c => c.LastAccess)
            .ToListAsync();

    /// <summary>
    /// Usa l'indice IX_CacheEntries_Expiry.
    /// La query è O(log n) anche con migliaia di entry in cache.
    /// </summary>
    public async Task<IEnumerable<CacheEntryEntity>> GetExpiredAsync()
        => await _db.CacheEntries
            .AsNoTracking()
            .Where(c => c.ExpiryTimestamp < DateTime.UtcNow)
            .ToListAsync();

    public async Task<int> CountActiveAsync()
        => await _db.CacheEntries
            .CountAsync(c => c.ExpiryTimestamp >= DateTime.UtcNow);

    public async Task AddAsync(CacheEntryEntity entity)
        => await _db.CacheEntries.AddAsync(entity);

    public void Update(CacheEntryEntity entity)
        => _db.CacheEntries.Update(entity);

    public void Remove(CacheEntryEntity entity)
        => _db.CacheEntries.Remove(entity);
}
