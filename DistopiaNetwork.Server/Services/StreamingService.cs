using DistopiaNetwork.Server.Configuration;
using DistopiaNetwork.Shared.Crypto;
using DistopiaNetwork.Shared.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Orchestrates the full streaming workflow (Sections 8–10 of the spec):
/// 1. Check local cache → serve immediately
/// 2. Cache miss → fetch from publisher server
/// 3. Publisher server also missing → fetch from publisher client
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
        IOptions<ServerSettings> opts,
        ILogger<StreamingService> logger)
    {
        _catalog = catalog;
        _cache = cache;
        _httpFactory = httpFactory;
        _settings = opts.Value;
        _logger = logger;
    }

    /// <summary>
    /// Resolve and return a Stream for the requested podcast's MP3.
    /// Returns null if the file cannot be obtained.
    /// </summary>
    public async Task<(Stream? Stream, string? ContentType, long? Length)> ResolveStreamAsync(
        string podcastId, CancellationToken ct = default)
    {
        var metadata = _catalog.Get(podcastId);
        if (metadata is null)
        {
            _logger.LogWarning("Stream request for unknown podcast: {Id}", podcastId);
            return (null, null, null);
        }

        // Step 2 – Local cache hit
        if (_cache.Has(metadata.FileHash))
        {
            _logger.LogInformation("Cache HIT for {Hash}. Streaming locally.", metadata.FileHash);
            var fs = _cache.OpenRead(metadata.FileHash);
            return (fs, "audio/mpeg", fs?.Length);
        }

        _logger.LogInformation("Cache MISS for {Hash}. Fetching from publisher server: {Server}",
            metadata.FileHash, metadata.PublisherServer);

        // Step 3 – Request from publisher server
        var data = await FetchFromPublisherServerAsync(metadata, ct);
        if (data is null) return (null, null, null);

        // Verify integrity
        if (!CryptoHelper.VerifyFileHash(data, metadata.FileHash))
        {
            _logger.LogError("File hash mismatch for {Hash}!", metadata.FileHash);
            return (null, null, null);
        }

        await _cache.StoreBytesAsync(metadata.FileHash, data);
        return (new MemoryStream(data), "audio/mpeg", data.Length);
    }

    private async Task<byte[]?> FetchFromPublisherServerAsync(
        PodcastMetadata metadata, CancellationToken ct)
    {
        // Find peer URL that matches publisher_server id
        var publisherUrl = FindPeerUrl(metadata.PublisherServer);
        if (publisherUrl is null)
        {
            _logger.LogWarning("No peer URL found for publisher server: {Server}", metadata.PublisherServer);
            return null;
        }

        try
        {
            var http = _httpFactory.CreateClient();
            var requestBody = JsonSerializer.Serialize(new
            {
                file_hash = metadata.FileHash,
                requesting_server_id = _settings.ServerId
            });

            var content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json");
            var url = $"{publisherUrl.TrimEnd('/')}/internal/file/{metadata.FileHash}";

            _logger.LogDebug("Requesting file from peer: {Url}", url);
            var response = await http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Peer {Url} returned {Status}", url, response.StatusCode);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching from publisher server {Server}", metadata.PublisherServer);
            return null;
        }
    }

    private string? FindPeerUrl(string serverId)
    {
        // Convention: peer URL contains the server ID, or use first match
        // In production, a registry/DNS would resolve this
        return _settings.PeerServers.FirstOrDefault(p =>
            p.Contains(serverId, StringComparison.OrdinalIgnoreCase))
            ?? _settings.PeerServers.FirstOrDefault();
    }
}
