using System.Collections.Concurrent;
using System.Text.Json;
using DistopiaNetwork.Server.Configuration;
using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// BackgroundService che sincronizza periodicamente il catalogo metadati con tutti i peer.
/// 
/// NOTA: È un Singleton (BackgroundService lo è per natura) ma CatalogService è ora Scoped.
/// Usa IServiceScopeFactory per creare uno scope dedicato ad ogni ciclo di sync —
/// pattern standard per risolvere la Captive Dependency Singleton → Scoped.
/// </summary>
public class SyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ServerSettings _settings;
    private readonly ILogger<SyncService> _logger;

    // Timestamp dell'ultima sync riuscita per ogni peer (cursore incrementale)
    private readonly ConcurrentDictionary<string, long> _lastSync = new();

    public SyncService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpFactory,
        IOptions<ServerSettings> settings,
        ILogger<SyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncService started. Interval: {Interval}s", _settings.SyncIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SyncAllPeersAsync(stoppingToken);

            // stoppingToken passato a Delay: se il server viene fermato,
            // il Delay lancia OperationCanceledException e usciamo pulitamente
            await Task.Delay(TimeSpan.FromSeconds(_settings.SyncIntervalSeconds), stoppingToken);
        }
    }

    private async Task SyncAllPeersAsync(CancellationToken ct)
    {
        foreach (var peerUrl in _settings.PeerServers)
        {
            try
            {
                await SyncFromPeerAsync(peerUrl, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sync failed for peer {Peer}", peerUrl);
                // Continua con il prossimo peer — un peer offline non blocca gli altri
            }
        }
    }

    private async Task SyncFromPeerAsync(string peerBaseUrl, CancellationToken ct)
    {
        var since = _lastSync.GetValueOrDefault(peerBaseUrl, 0);
        var url = $"{peerBaseUrl}/podcasts/since/{since}";

        var http = _httpFactory.CreateClient();
        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var syncResponse = JsonSerializer.Deserialize<SyncResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (syncResponse?.Podcasts is null || syncResponse.Podcasts.Count == 0)
        {
            _logger.LogDebug("No new podcasts from {Peer} since {Since}", peerBaseUrl, since);
            return;
        }

        // Crea uno scope Scoped per CatalogService — risolve la Captive Dependency
        using var scope = _scopeFactory.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogService>();

        int added = 0;
        foreach (var podcast in syncResponse.Podcasts)
        {
            if (await catalog.TryAddOrUpdateAsync(podcast, ct))
                added++;
        }

        _lastSync[peerBaseUrl] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _logger.LogInformation("Sync from {Peer}: {Added}/{Total} podcasts added.", peerBaseUrl, added, syncResponse.Podcasts.Count);
    }
}
