using DistopiaNetwork.Server.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// BackgroundService che esegue la garbage collection della cache ogni ora.
/// 
/// NOTA: Come SyncService, è Singleton ma dipende da CacheService (Scoped).
/// Usa IServiceScopeFactory per creare uno scope dedicato per ogni ciclo di cleanup.
/// 
/// Pattern "sleep first": il primo cleanup avviene dopo il primo intervallo,
/// non all'avvio — per non rallentare lo startup del server.
/// </summary>
public class CacheCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly int _intervalSeconds;
    private readonly ILogger<CacheCleanupService> _logger;

    public CacheCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<ServerSettings> settings,
        ILogger<CacheCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _intervalSeconds = settings.Value.CacheCleanupIntervalSeconds;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CacheCleanupService started. Interval: {Interval}s", _intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Sleep prima del cleanup (pattern "sleep first")
            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);

            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cache cleanup.");
            }
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var cacheService = scope.ServiceProvider.GetRequiredService<CacheService>();

        var removed = await cacheService.CleanExpiredAsync(ct);
        _logger.LogInformation("Cache cleanup complete: {Count} files removed.", removed);
    }
}
