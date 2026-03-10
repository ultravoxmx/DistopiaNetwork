using DistopiaNetwork.Server.Configuration;
using DistopiaNetwork.Shared.Dto;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Background service that periodically pulls new metadata from all known peer servers.
/// Implements Section 5 of the spec: GET /podcasts/since/{timestamp}
/// </summary>
public class SyncService : BackgroundService
{
    private readonly CatalogService _catalog;
    private readonly ServerSettings _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<SyncService> _logger;

    // Track last-sync timestamp per peer
    private readonly Dictionary<string, long> _lastSync = new();

    public SyncService(
        CatalogService catalog,
        IOptions<ServerSettings> opts,
        IHttpClientFactory httpFactory,
        ILogger<SyncService> logger)
    {
        _catalog = catalog;
        _settings = opts.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncService started. Interval: {Sec}s, Peers: {Peers}",
            _settings.SyncIntervalSeconds, string.Join(", ", _settings.PeerServers));

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncAllPeersAsync();
            await Task.Delay(TimeSpan.FromSeconds(_settings.SyncIntervalSeconds), stoppingToken);
        }
    }

    private async Task SyncAllPeersAsync()
    {
        foreach (var peer in _settings.PeerServers)
        {
            try
            {
                await SyncFromPeerAsync(peer);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Sync failed for peer {Peer}: {Error}", peer, ex.Message);
            }
        }
    }

    private async Task SyncFromPeerAsync(string peerBaseUrl)
    {
        var since = _lastSync.GetValueOrDefault(peerBaseUrl, 0);
        var http = _httpFactory.CreateClient();
        var url = $"{peerBaseUrl.TrimEnd('/')}/podcasts/since/{since}";

        _logger.LogDebug("Syncing from {Url}", url);

        var response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var syncResponse = JsonSerializer.Deserialize<SyncResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (syncResponse?.Podcasts is null) return;

        int added = 0;
        foreach (var podcast in syncResponse.Podcasts)
        {
            if (_catalog.TryAddFromSync(podcast))
                added++;
        }

        _lastSync[peerBaseUrl] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _logger.LogInformation("Sync from {Peer}: {Added} new podcasts.", peerBaseUrl, added);
    }
}
