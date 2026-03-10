using DistopiaNetwork.Server.Configuration;
using DistopiaNetwork.Server.Data.UnitOfWork;
using DistopiaNetwork.Server.Entities;
using DistopiaNetwork.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Gestisce la cache temporanea degli MP3 su filesystem + DB.
///
/// Due livelli di stato sincronizzati:
///   - Filesystem: file fisici in CacheDirectory (es. ./mp3cache/abc123.mp3)
///   - SQL Server:  tabella CacheEntries con hash, path, TTL, last access
///
/// Strategia di eviction:
///   - Lazy: Has() rimuove entry scadute al momento dell'accesso
///   - Eager: CacheCleanupService chiama CleanExpiredAsync() ogni ora
/// </summary>
public class CacheService
{
    private readonly IUnitOfWork _uow;
    private readonly string _cacheDir;
    private readonly ILogger<CacheService> _logger;

    public static readonly TimeSpan MinTtl = TimeSpan.FromDays(1);
    public static readonly TimeSpan MaxTtl = TimeSpan.FromDays(7);

    public CacheService(IUnitOfWork uow, IOptions<ServerSettings> settings, ILogger<CacheService> logger)
    {
        _uow = uow;
        _cacheDir = settings.Value.CacheDirectory;
        _logger = logger;

        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// Verifica se un file è in cache e non scaduto.
    /// Implementa lazy eviction: rimuove l'entry dal DB se scaduta o se il file fisico manca.
    /// </summary>
    public async Task<bool> HasAsync(string fileHash)
    {
        var entry = await _uow.CacheEntries.GetByHashAsync(fileHash);
        if (entry is null) return false;

        // Lazy eviction: entry scaduta
        if (entry.ExpiryTimestamp < DateTime.UtcNow)
        {
            await RemoveAsync(entry);
            return false;
        }

        // Difesa contro cancellazioni manuali del filesystem
        if (!File.Exists(entry.FilePath))
        {
            _logger.LogWarning("Cache entry {Hash} exists in DB but file is missing: {Path}", fileHash, entry.FilePath);
            _uow.CacheEntries.Remove(entry);
            await _uow.SaveChangesAsync();
            return false;
        }

        return true;
    }

    /// <summary>
    /// Apre un FileStream in lettura e resetta il TTL dell'entry.
    /// Ritorna null se il file non è in cache.
    /// </summary>
    public async Task<FileStream?> OpenReadAsync(string fileHash)
    {
        var entry = await _uow.CacheEntries.GetByHashAsync(fileHash);
        if (entry is null || !File.Exists(entry.FilePath)) return null;

        // Reset TTL: un file letto rimane in cache per altri MaxTtl giorni
        entry.LastAccess = DateTime.UtcNow;
        entry.ExpiryTimestamp = DateTime.UtcNow + MaxTtl;
        _uow.CacheEntries.Update(entry);
        await _uow.SaveChangesAsync();

        return new FileStream(entry.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <summary>
    /// Salva un MP3 da Stream su disco e registra l'entry nel DB.
    /// Il nome del file è sempre {fileHash}.mp3 — univoco per definizione.
    /// </summary>
    public async Task<CacheEntryEntity> StoreAsync(string fileHash, Stream mp3Stream, string? podcastId = null)
    {
        var filePath = Path.Combine(_cacheDir, $"{fileHash}.mp3");

        await using var fs = File.Create(filePath);
        await mp3Stream.CopyToAsync(fs);

        return await RegisterEntryAsync(fileHash, filePath, podcastId);
    }

    /// <summary>
    /// Salva un MP3 da array di byte su disco e registra l'entry nel DB.
    /// </summary>
    public async Task<CacheEntryEntity> StoreBytesAsync(string fileHash, byte[] data, string? podcastId = null)
    {
        var filePath = Path.Combine(_cacheDir, $"{fileHash}.mp3");
        await File.WriteAllBytesAsync(filePath, data);

        return await RegisterEntryAsync(fileHash, filePath, podcastId);
    }

    /// <summary>
    /// Ritorna il percorso fisico di un file in cache, null se non presente.
    /// </summary>
    public async Task<string?> GetFilePathAsync(string fileHash)
    {
        var entry = await _uow.CacheEntries.GetByHashAsync(fileHash);
        return entry is not null && File.Exists(entry.FilePath) ? entry.FilePath : null;
    }

    /// <summary>
    /// Rimuove tutti i file scaduti dal filesystem e dal DB.
    /// Chiamato da CacheCleanupService ogni ora.
    /// </summary>
    public async Task<int> CleanExpiredAsync(CancellationToken ct = default)
    {
        var expired = (await _uow.CacheEntries.GetExpiredAsync()).ToList();

        foreach (var entry in expired)
            await RemoveAsync(entry);

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation("Cache cleanup: removed {Count} expired entries.", expired.Count);
        return expired.Count;
    }

    /// <summary>
    /// Numero di file attivi in cache (non scaduti).
    /// </summary>
    public async Task<int> CountActiveAsync()
        => await _uow.CacheEntries.CountActiveAsync();

    // ── Helpers privati ───────────────────────────────────────────────────────

    private async Task<CacheEntryEntity> RegisterEntryAsync(string fileHash, string filePath, string? podcastId)
    {
        // Se esiste già (upload ripetuto), aggiorna invece di inserire
        var existing = await _uow.CacheEntries.GetByHashAsync(fileHash);
        if (existing is not null)
        {
            existing.FilePath = filePath;
            existing.LastAccess = DateTime.UtcNow;
            existing.ExpiryTimestamp = DateTime.UtcNow + MaxTtl;
            existing.PodcastId = podcastId ?? existing.PodcastId;
            _uow.CacheEntries.Update(existing);
            await _uow.SaveChangesAsync();
            return existing;
        }

        var entry = new CacheEntryEntity
        {
            FileHash        = fileHash,
            FilePath        = filePath,
            LastAccess      = DateTime.UtcNow,
            ExpiryTimestamp = DateTime.UtcNow + MaxTtl,
            PodcastId       = podcastId,
        };

        await _uow.CacheEntries.AddAsync(entry);
        await _uow.SaveChangesAsync();
        return entry;
    }

    private async Task RemoveAsync(CacheEntryEntity entry)
    {
        if (File.Exists(entry.FilePath))
        {
            try { File.Delete(entry.FilePath); }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not delete cached file {Path}", entry.FilePath);
            }
        }
        _uow.CacheEntries.Remove(entry);
    }
}
