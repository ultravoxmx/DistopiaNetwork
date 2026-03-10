using Microsoft.EntityFrameworkCore;
using DistopiaNetwork.Server.Entities;

namespace DistopiaNetwork.Server.Data.Repositories;

/// <summary>
/// Implementazione concreta di IPodcastRepository basata su EF Core + SQL Server.
/// AsNoTracking() su tutte le query di lettura: i metadati sono immutabili dopo la firma,
/// non serve il change tracking per le operazioni di sola lettura.
/// </summary>
public class PodcastRepository : IPodcastRepository
{
    private readonly AppDbContext _db;

    public PodcastRepository(AppDbContext db) => _db = db;

    public async Task<PodcastEntity?> GetByIdAsync(string id)
        => await _db.Podcasts.FindAsync(id);

    public async Task<IEnumerable<PodcastEntity>> GetAllAsync()
        => await _db.Podcasts
            .AsNoTracking()
            .OrderByDescending(p => p.PublishTimestamp)
            .ToListAsync();

    /// <summary>
    /// Usa l'indice IX_Podcasts_PublishTimestamp per O(log n) anche su milioni di righe.
    /// I risultati sono ordinati per timestamp per garantire consistenza nella sync.
    /// </summary>
    public async Task<IEnumerable<PodcastEntity>> GetSinceAsync(long unixTimestamp)
        => await _db.Podcasts
            .AsNoTracking()
            .Where(p => p.PublishTimestamp > unixTimestamp)
            .OrderBy(p => p.PublishTimestamp)
            .ToListAsync();

    /// <summary>
    /// Usa l'indice IX_Podcasts_FileHash per la ricerca del duplicato.
    /// Ritorna null se non trovato — il chiamante decide come gestire il caso.
    /// </summary>
    public async Task<PodcastEntity?> FindByFileHashAndPublisherAsync(
        string fileHash, string publisherPubKey)
        => await _db.Podcasts
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.FileHash == fileHash &&
                p.PublisherPubKey == publisherPubKey);

    public async Task<bool> ExistsByFileHashAsync(string fileHash)
        => await _db.Podcasts
            .AnyAsync(p => p.FileHash == fileHash);

    public async Task<IEnumerable<PodcastEntity>> GetByPublisherServerAsync(string serverId)
        => await _db.Podcasts
            .AsNoTracking()
            .Where(p => p.PublisherServer == serverId)
            .ToListAsync();

    public async Task AddAsync(PodcastEntity entity)
        => await _db.Podcasts.AddAsync(entity);

    public void Update(PodcastEntity entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        _db.Podcasts.Update(entity);
    }

    public void Remove(PodcastEntity entity)
        => _db.Podcasts.Remove(entity);
}
