using System.Net.Http;
using DistopiaNetwork.Server.Configuration;
using DistopiaNetwork.Shared.Crypto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Orchestratore del flusso di streaming a cascata.
/// Dato un podcast_id (o un fileHash diretto), restituisce uno Stream dell'MP3
/// seguendo questa catena di fallback:
///
///   1. Cache locale → stream immediato, reset TTL
///   2. Publisher server (peer) → fetch remoto, cache locale, stream
///   3. (Se il peer non ha il file → il peer lo chiederà al publisher client)
///
/// Ritorna (null, null, null) se il file non è recuperabile.
/// </summary>
public class StreamingService
{
    private readonly CatalogService _catalog;
    private readonly CacheService _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ServerSettings _settings;
    private readonly ILogger<StreamingService> _logger;

    public StreamingService(
        CatalogService catalog,
        CacheService cache,
        IHttpClientFactory httpFactory,
        IOptions<ServerSettings> settings,
        ILogger<StreamingService> logger)
    {
        _catalog    = catalog;
        _cache      = cache;
        _httpFactory = httpFactory;
        _settings   = settings.Value;
        _logger     = logger;
    }

    /// <summary>
    /// Risolve lo stream dato un podcast_id.
    /// Ritorna (Stream, contentType, length) oppure (null, null, null).
    /// </summary>
    public async Task<(Stream? stream, string? contentType, long? length)> ResolveStreamAsync(
        string podcastId, CancellationToken ct)
    {
        var metadata = await _catalog.GetAsync(podcastId);
        if (metadata is null)
        {
            _logger.LogWarning("Podcast {Id} not found in catalog.", podcastId);
            return (null, null, null);
        }

        return await ResolveStreamByHashAsync(metadata.FileHash, ct, metadata.PublisherServer);
    }

    /// <summary>
    /// Risolve lo stream dato direttamente un fileHash SHA-256.
    /// Usato dall'endpoint /internal/file/{fileHash} per le richieste server-to-server.
    /// </summary>
    public async Task<(Stream? stream, string? contentType, long? length)> ResolveStreamByHashAsync(
        string fileHash, CancellationToken ct, string? publisherServerId = null)
    {
        // ── CASO 1: file in cache locale ──────────────────────────────────────
        if (await _cache.HasAsync(fileHash))
        {
            var fs = await _cache.OpenReadAsync(fileHash);
            if (fs is not null)
            {
                _logger.LogDebug("Cache hit for {Hash}", fileHash);
                return (fs, "audio/mpeg", fs.Length);
            }
        }

        // ── CASO 2: file mancante → chiedi al publisher server ────────────────
        _logger.LogInformation("Cache miss for {Hash}. Fetching from publisher server.", fileHash);

        var data = await FetchFromPublisherServerAsync(fileHash, publisherServerId, ct);
        if (data is null)
        {
            _logger.LogWarning("Could not retrieve {Hash} from any peer.", fileHash);
            return (null, null, null);
        }

        // Verifica integrità prima di cachare (difesa contro corruzione di rete)
        if (!CryptoHelper.VerifyFileHash(data, fileHash))
        {
            _logger.LogError("Hash mismatch for {Hash} received from peer. Discarding.", fileHash);
            return (null, null, null);
        }

        await _cache.StoreBytesAsync(fileHash, data);
        _logger.LogInformation("Cached {Hash} ({Bytes} bytes).", fileHash, data.Length);

        return (new MemoryStream(data), "audio/mpeg", data.Length);
    }

    // ── Helpers privati ───────────────────────────────────────────────────────

    /// <summary>
    /// Esegue GET /internal/file/{fileHash} sul publisher server.
    /// Se publisherServerId è null, prova tutti i peer configurati.
    /// </summary>
    private async Task<byte[]?> FetchFromPublisherServerAsync(
        string fileHash, string? publisherServerId, CancellationToken ct)
    {
        // Determina quali peer interrogare
        var peers = publisherServerId is not null
            ? GetPeerUrls(publisherServerId)   // prima il publisher server, poi gli altri
            : _settings.PeerServers.ToList();

        foreach (var peerUrl in peers)
        {
            try
            {
                var http = _httpFactory.CreateClient();
                var url  = $"{peerUrl}/internal/file/{fileHash}";

                _logger.LogDebug("Requesting {Hash} from {Url}", fileHash, url);

                var response = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsByteArrayAsync(ct);

                _logger.LogDebug("Peer {Url} returned {Status} for {Hash}", url, response.StatusCode, fileHash);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch {Hash} from peer {Peer}", fileHash, peerUrl);
            }
        }

        return null;
    }

    /// <summary>
    /// Ritorna la lista di peer URL da interrogare, mettendo per primo il publisher server.
    /// Usa convenzione: l'URL del peer contiene il suo server ID come sottostringa.
    /// </summary>
    private List<string> GetPeerUrls(string publisherServerId)
    {
        var peers = _settings.PeerServers.ToList();

        // Trova il publisher server nella lista dei peer
        var publisherUrl = peers.FirstOrDefault(p =>
            p.Contains(publisherServerId, StringComparison.OrdinalIgnoreCase));

        if (publisherUrl is not null)
        {
            // Metti il publisher server per primo per ridurre latenza
            peers.Remove(publisherUrl);
            peers.Insert(0, publisherUrl);
        }

        return peers;
    }
}
