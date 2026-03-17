using DistopiaNetwork.Server.Configuration;
using DistopiaNetwork.Shared.Crypto;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Orchestratore del flusso di streaming a cascata.
/// Dato un podcast_id (o un fileHash diretto), restituisce uno Stream dell'MP3
/// seguendo questa catena di fallback:
///
///   1. Cache locale → stream immediato, reset TTL
///   2. Publisher server (peer) → fetch HTTP remoto, cache locale, stream
///   3. Publisher client via WebSocket → richiesta diretta al PC del creator,
///      cache locale, stream  ← NUOVO (gestisce NAT/rete privata)
///
/// Il Caso 3 si attiva SOLO sul publisher server (il server a cui il creator
/// è connesso). Gli altri server passano sempre dal Caso 2.
///
/// Ritorna (null, null, null) se il file non è recuperabile.
/// </summary>
public class StreamingService
{
    private readonly CatalogService _catalog;
    private readonly CacheService _cache;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PublisherConnectionManager _publisherConnections;
    private readonly ServerSettings _settings;
    private readonly ILogger<StreamingService> _logger;

    public StreamingService(
        CatalogService catalog,
        CacheService cache,
        IHttpClientFactory httpFactory,
        PublisherConnectionManager publisherConnections,
        IOptions<ServerSettings> settings,
        ILogger<StreamingService> logger)
    {
        _catalog              = catalog;
        _cache                = cache;
        _httpFactory          = httpFactory;
        _publisherConnections = publisherConnections;
        _settings             = settings.Value;
        _logger               = logger;
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

        return await ResolveStreamByHashAsync(
            metadata.FileHash, ct,
            publisherServerId: metadata.PublisherServer,
            publisherPubKey: metadata.PublisherPubKey);
    }

    /// <summary>
    /// Risolve lo stream dato direttamente un fileHash SHA-256.
    /// Usato dall'endpoint /internal/file/{fileHash} per le richieste server-to-server.
    /// </summary>
    public async Task<(Stream? stream, string? contentType, long? length)> ResolveStreamByHashAsync(
        string fileHash,
        CancellationToken ct,
        string? publisherServerId = null,
        string? publisherPubKey = null)
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

        // ── CASO 2: chiedi al publisher server (peer HTTP) ────────────────────
        _logger.LogInformation("Cache miss for {Hash}. Fetching from publisher server.", fileHash);

        var data = await FetchFromPublisherServerAsync(fileHash, publisherServerId, ct);

        // ── CASO 3: il peer non ce l'ha → chiedi al publisher client via WS ──
        // Si attiva SOLO se siamo il publisher server (publisherServerId == nostro ServerId)
        // e il publisher client è connesso via WebSocket.
        if (data is null && IsPublisherServer(publisherServerId) && publisherPubKey is not null)
        {
            _logger.LogInformation(
                "Peer fetch failed. Requesting {Hash} from publisher client via WebSocket.", fileHash);

            data = await FetchFromPublisherClientAsync(publisherPubKey, fileHash, ct);
        }

        if (data is null)
        {
            _logger.LogWarning("Could not retrieve {Hash} from any source.", fileHash);
            return (null, null, null);
        }

        // Verifica integrità prima di cachare
        if (!CryptoHelper.VerifyFileHash(data, fileHash))
        {
            _logger.LogError("Hash mismatch for {Hash}. Discarding.", fileHash);
            return (null, null, null);
        }

        // File vuoto = publisher ha risposto FILE_NOT_FOUND
        if (data.Length == 0)
        {
            _logger.LogWarning("Publisher client reported FILE_NOT_FOUND for {Hash}.", fileHash);
            return (null, null, null);
        }

        await _cache.StoreBytesAsync(fileHash, data);
        _logger.LogInformation("Cached {Hash} ({Bytes} bytes).", fileHash, data.Length);

        return (new MemoryStream(data), "audio/mpeg", data.Length);
    }

    // ── Helpers privati ───────────────────────────────────────────────────────

    /// <summary>
    /// Esegue GET /internal/file/{fileHash} sul publisher server peer.
    /// </summary>
    private async Task<byte[]?> FetchFromPublisherServerAsync(
        string fileHash, string? publisherServerId, CancellationToken ct)
    {
        var peers = publisherServerId is not null
            ? GetPeerUrls(publisherServerId)
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

                _logger.LogDebug("Peer {Url} returned {Status} for {Hash}",
                    url, response.StatusCode, fileHash);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch {Hash} from peer {Peer}", fileHash, peerUrl);
            }
        }

        return null;
    }

    /// <summary>
    /// Richiede il file al publisher client tramite WebSocket.
    /// Funziona anche se il client è dietro NAT perché è il client
    /// ad aver aperto la connessione verso il server.
    /// </summary>
    private async Task<byte[]?> FetchFromPublisherClientAsync(
        string publisherPubKey, string fileHash, CancellationToken ct)
    {
        if (!_publisherConnections.IsConnected(publisherPubKey))
        {
            _logger.LogWarning(
                "Publisher client not connected via WebSocket. Cannot retrieve {Hash}.", fileHash);
            return null;
        }

        return await _publisherConnections.RequestFileAsync(publisherPubKey, fileHash, ct: ct);
    }

    /// <summary>
    /// Verifica se questo server è il publisher server per il podcast richiesto.
    /// Solo il publisher server può contattare direttamente il client.
    /// </summary>
    private bool IsPublisherServer(string? publisherServerId)
        => publisherServerId is not null
           && publisherServerId.Equals(_settings.ServerId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ritorna la lista di peer URL ordinata con il publisher server per primo.
    /// </summary>
    private List<string> GetPeerUrls(string publisherServerId)
    {
        var peers = _settings.PeerServers.ToList();

        var publisherUrl = peers.FirstOrDefault(p =>
            p.Contains(publisherServerId, StringComparison.OrdinalIgnoreCase));

        if (publisherUrl is not null)
        {
            peers.Remove(publisherUrl);
            peers.Insert(0, publisherUrl);
        }

        return peers;
    }
}
