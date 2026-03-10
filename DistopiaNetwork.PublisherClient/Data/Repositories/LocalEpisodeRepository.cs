using DistopiaNetwork.PublisherClient.Entities;
using Microsoft.EntityFrameworkCore;

namespace DistopiaNetwork.PublisherClient.Data.Repositories;

/// <summary>
/// Implementazione concreta di ILocalEpisodeRepository su SQLite.
/// Il client è un'applicazione console con un unico utente: non servono
/// ConcurrentDictionary o cache L1, EF Core è già sufficiente.
/// </summary>
public class LocalEpisodeRepository : ILocalEpisodeRepository
{
    private readonly PublisherDbContext _db;

    public LocalEpisodeRepository(PublisherDbContext db) => _db = db;

    public async Task<LocalEpisodeEntity?> GetByIdAsync(string podcastId)
        => await _db.Episodes.FindAsync(podcastId);

    public async Task<IEnumerable<LocalEpisodeEntity>> GetAllAsync()
        => await _db.Episodes
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

    /// <summary>
    /// Usa l'indice UNIQUE UX_Episodes_FileHash per lookup O(log n).
    /// </summary>
    public async Task<LocalEpisodeEntity?> FindByHashAsync(string fileHash)
        => await _db.Episodes
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.FileHash == fileHash);

    public async Task<IEnumerable<LocalEpisodeEntity>> GetPendingAsync()
        => await _db.Episodes
            .AsNoTracking()
            .Where(e => e.UploadStatus == UploadStatuses.Draft ||
                        e.UploadStatus == UploadStatuses.Failed)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();

    public async Task<IEnumerable<LocalEpisodeEntity>> GetAllByStatusAsync(string status)
        => await _db.Episodes
            .AsNoTracking()
            .Where(e => e.UploadStatus == status)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

    public async Task AddAsync(LocalEpisodeEntity entity)
        => await _db.Episodes.AddAsync(entity);

    public void Update(LocalEpisodeEntity entity)
        => _db.Episodes.Update(entity);

    public void Remove(LocalEpisodeEntity entity)
        => _db.Episodes.Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
