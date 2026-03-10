using DistopiaNetwork.Server.Configuration;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Background service that periodically scans the cache and removes expired MP3 files.
/// Implements Section 7 and 17.6 of the spec.
/// </summary>
public class CacheCleanupService : BackgroundService
{
    private readonly CacheService _cache;
    private readonly int _intervalSeconds;
    private readonly ILogger<CacheCleanupService> _logger;

    public CacheCleanupService(
        CacheService cache,
        IOptions<ServerSettings> opts,
        ILogger<CacheCleanupService> logger)
    {
        _cache = cache;
        _intervalSeconds = opts.Value.CacheCleanupIntervalSeconds;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CacheCleanupService started. Interval: {Sec}s", _intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(_intervalSeconds), stoppingToken);
            _logger.LogDebug("Running cache cleanup scan...");
            _cache.CleanExpired();
        }
    }
}
