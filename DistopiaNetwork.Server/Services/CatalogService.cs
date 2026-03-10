using System.Collections.Concurrent;
using DistopiaNetwork.Server.Data.UnitOfWork;
using DistopiaNetwork.Server.Entities;
using DistopiaNetwork.Shared.Crypto;
using DistopiaNetwork.Shared.Models;
using Microsoft.Extensions.Logging;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Gestisce il catalogo globale dei podcast.
/// Usa IUnitOfWork per la persistenza su SQL Server.
/// 
/// Strategia di caching a due livelli:
///   L1 = ConcurrentDictionary in memoria (accesso ~0ms, si azzera al riavvio)
///   L2 = SQL Server (persistente, sopravvive ai riavvii)
/// 
/// Le letture cercano prima in L1, poi in L2.
/// Le scritture aggiornano sempre entrambi i livelli.
/// </summary>
public class CatalogService
{
    private readonly IUnitOfWork _uow;
    private readonly ILogger<CatalogService> _logger;

    // Cache L1 in-memory: evita hit al DB per ogni richiesta di streaming
    private readonly ConcurrentDictionary<string, PodcastMetadata> _memCache = new();

    public CatalogService(IUnitOfWork uow, ILogger<CatalogService> logger)
    {
        _uow = uow;
        _logger = logger;
    }

    /// <summary>
    /// Tenta di aggiungere o aggiornare un episodio nel catalogo.
    /// Esegue: verifica firma → rilevazione duplicati → persistenza DB → aggiornamento L1.
    /// Ritorna false se la firma è invalida o l'episodio è un duplicato.
    /// </summary>
    public async Task<bool> TryAddOrUpdateAsync(PodcastMetadata metadata, CancellationToken ct = default)
    {
        // 1. Verifica firma crittografica (invariata dalla v1)
        if (!CryptoHelper.VerifyMetadata(metadata, metadata.PublisherPubKey))
        {
            _logger.LogWarning("Rejected {Id}: invalid signature.", metadata.PodcastId);
            return false;
        }

        // 2. Rilevazione duplicati: stesso hash, stesso publisher, ID diverso
        var dup = await _uow.Podcasts.FindByFileHashAndPublisherAsync(
            metadata.FileHash, metadata.PublisherPubKey);

        if (dup is not null && dup.PodcastId != metadata.PodcastId)
        {
            _logger.LogWarning("Rejected {NewId}: hash already exists as {ExistingId}.",
                metadata.PodcastId, dup.PodcastId);
            return false;
        }

        // 3. Upsert: aggiorna se esiste, inserisce se nuovo
        var existing = await _uow.Podcasts.GetByIdAsync(metadata.PodcastId);
        if (existing is null)
            await _uow.Podcasts.AddAsync(MapToEntity(metadata));
        else
            _uow.Podcasts.Update(MapToEntity(metadata));

        await _uow.SaveChangesAsync(ct);

        // 4. Aggiorna cache L1
        _memCache[metadata.PodcastId] = metadata;

        _logger.LogInformation("Podcast {Id} '{Title}' saved to catalog.", metadata.PodcastId, metadata.Title);
        return true;
    }

    /// <summary>
    /// Recupera un episodio per ID. Cerca prima in L1, poi nel DB.
    /// </summary>
    public async Task<PodcastMetadata?> GetAsync(string podcastId)
    {
        // Hit L1
        if (_memCache.TryGetValue(podcastId, out var cached))
            return cached;

        // Miss L1 → cerca nel DB e popola L1
        var entity = await _uow.Podcasts.GetByIdAsync(podcastId);
        if (entity is null) return null;

        var metadata = MapToModel(entity);
        _memCache[podcastId] = metadata;
        return metadata;
    }

    /// <summary>
    /// Ritorna tutti gli episodi del catalogo.
    /// </summary>
    public async Task<IEnumerable<PodcastMetadata>> GetAllAsync()
        => (await _uow.Podcasts.GetAllAsync()).Select(MapToModel);

    /// <summary>
    /// Ritorna gli episodi pubblicati dopo il timestamp dato.
    /// Usato da SyncService per la sincronizzazione incrementale.
    /// </summary>
    public async Task<IEnumerable<PodcastMetadata>> GetSinceAsync(long unixTimestamp)
        => (await _uow.Podcasts.GetSinceAsync(unixTimestamp)).Select(MapToModel);

    /// <summary>
    /// Cerca un episodio per FileHash (qualsiasi publisher).
    /// Usato per il controllo duplicati nel controller prima dell'upload.
    /// </summary>
    public async Task<PodcastMetadata?> FindByFileHashAsync(string fileHash)
    {
        // Cerca in L1 prima
        var inMem = _memCache.Values.FirstOrDefault(p => p.FileHash == fileHash);
        if (inMem is not null) return inMem;

        // Cerca nel DB
        var entity = await _uow.Podcasts
            .FindByFileHashAndPublisherAsync(fileHash, string.Empty);

        // FindByFileHashAndPublisher richiede publisher: usiamo ExistsByFileHash
        if (!await _uow.Podcasts.ExistsByFileHashAsync(fileHash)) return null;

        // Recupera l'entità completa via query diretta
        var all = await _uow.Podcasts.GetAllAsync();
        var found = all.FirstOrDefault(p => p.FileHash == fileHash);
        return found is null ? null : MapToModel(found);
    }

    /// <summary>
    /// Ritorna il numero totale di episodi nel catalogo.
    /// </summary>
    public int Count() => _memCache.Count;

    // ── Mapping Entity ↔ Model ────────────────────────────────────────────────

    private static PodcastEntity MapToEntity(PodcastMetadata m) => new()
    {
        PodcastId       = m.PodcastId,
        PublisherPubKey = m.PublisherPubKey,
        PublisherServer = m.PublisherServer,
        Title           = m.Title,
        Description     = m.Description ?? string.Empty,
        ImageUrl        = m.ImageUrl,
        FileHash        = m.FileHash,
        FileSize        = m.FileSize,
        DurationSeconds = m.DurationSeconds,
        PublishTimestamp = m.PublishTimestamp,
        Signature       = m.Signature,
    };

    private static PodcastMetadata MapToModel(PodcastEntity e) => new()
    {
        PodcastId       = e.PodcastId,
        PublisherPubKey = e.PublisherPubKey,
        PublisherServer = e.PublisherServer,
        Title           = e.Title,
        Description     = e.Description,
        ImageUrl        = e.ImageUrl,
        FileHash        = e.FileHash,
        FileSize        = e.FileSize,
        DurationSeconds = e.DurationSeconds,
        PublishTimestamp = e.PublishTimestamp,
        Signature       = e.Signature,
    };
}
