using System.Collections.Concurrent;
using DistopiaNetwork.Server.Configuration;
using DistopiaNetwork.Shared.Models;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Gestisce la cache temporanea degli MP3 su disco.
/// Ogni accesso resetta il timer di scadenza (1-7 giorni).
/// </summary>
public class CacheService
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();
    private readonly string _cacheDir;
    private readonly ILogger<CacheService> _logger;

    public CacheService(IOptions<ServerSettings> opts, ILogger<CacheService> logger)
    {
        _cacheDir = opts.Value.CacheDirectory;
        _logger = logger;
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// Controlla se un file è in cache e non è scaduto.
    /// Applica lazy eviction: se scaduto lo rimuove subito.
    /// </summary>
    public bool Has(string fileHash)
    {
        if (!_entries.TryGetValue(fileHash, out var entry)) return false;
        if (entry.IsExpired)
        {
            Remove(fileHash);
            return false;
        }
        // Difesa contro cancellazioni manuali del file su disco
        return File.Exists(entry.FilePath);
    }

    /// <summary>
    /// Copia lo stream direttamente su disco senza buffering in RAM.
    /// Usato per upload da publisher (evita doppio buffering memoria).
    /// </summary>
    public async Task<CacheEntry> StoreAsync(
        string fileHash, Stream inputStream, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_cacheDir, $"{fileHash}.mp3");

        await using (var fs = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,          // 80 KB buffer — ottimale per I/O su disco
            useAsync: true))
        {
            await inputStream.CopyToAsync(fs, ct);
        }

        var entry = new CacheEntry { FileHash = fileHash, FilePath = filePath };
        entry.ResetExpiry();
        _entries[fileHash] = entry;

        var fileInfo = new FileInfo(filePath);
        _logger.LogInformation(
            "Cached: {Hash} → {Path} ({Size:N0} bytes)", fileHash, filePath, fileInfo.Length);
        return entry;
    }

    /// <summary>
    /// Salva byte array in cache (usato da StreamingService per file ricevuti da peer).
    /// </summary>
    public async Task<CacheEntry> StoreBytesAsync(
        string fileHash, byte[] data, CancellationToken ct = default)
    {
        using var ms = new MemoryStream(data, writable: false);
        return await StoreAsync(fileHash, ms, ct);
    }

    /// <summary>
    /// Apre il file in lettura e resetta il TTL.
    /// Ritorna null se il file non è in cache o è scaduto.
    /// </summary>
    public FileStream? OpenRead(string fileHash)
    {
        if (!_entries.TryGetValue(fileHash, out var entry) || entry.IsExpired)
            return null;
        entry.ResetExpiry();
        return new FileStream(
            entry.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,     // più client possono leggere simultaneamente
            bufferSize: 81920,
            useAsync: true);
    }

    /// <summary>Legge tutti i byte di un file cachato.</summary>
    public byte[]? ReadBytes(string fileHash)
    {
        if (!Has(fileHash)) return null;
        var entry = _entries[fileHash];
        entry.ResetExpiry();
        return File.ReadAllBytes(entry.FilePath);
    }

    /// <summary>Rimuove un file dalla cache (scaduto o eliminato esplicitamente).</summary>
    public void Remove(string fileHash)
    {
        if (_entries.TryRemove(fileHash, out var entry))
        {
            try { File.Delete(entry.FilePath); } catch { /* best effort */ }
            _logger.LogInformation("Cache evicted: {Hash}", fileHash);
        }
    }

    /// <summary>Cleanup periodico: rimuove tutti i file scaduti.</summary>
    public void CleanExpired()
    {
        // Materializza prima di modificare il dizionario — thread-safe
        var expired = _entries.Values
            .Where(e => e.IsExpired)
            .Select(e => e.FileHash)
            .ToList();

        foreach (var hash in expired)
            Remove(hash);

        if (expired.Count > 0)
            _logger.LogInformation(
                "Cache cleanup: removed {Count} expired entries.", expired.Count);
    }

    public IReadOnlyDictionary<string, CacheEntry> Entries => _entries;
}
