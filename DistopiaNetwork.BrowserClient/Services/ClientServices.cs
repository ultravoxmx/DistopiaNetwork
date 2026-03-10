using DistopiaNetwork.BrowserClient.Configuration;
using DistopiaNetwork.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DistopiaNetwork.BrowserClient.Services;

/// <summary>
/// Communicates with a server to browse the podcast catalog.
/// Browsers connect to any server (Section 2.3).
/// </summary>
public class CatalogClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _baseUrl;
    private readonly ILogger<CatalogClient> _logger;

    public CatalogClient(IHttpClientFactory httpFactory,
        IOptions<BrowserSettings> opts,
        ILogger<CatalogClient> logger)
    {
        _httpFactory = httpFactory;
        _baseUrl = opts.Value.ServerUrl.TrimEnd('/');
        _logger = logger;
    }

    public async Task<List<PodcastMetadata>> GetAllAsync()
    {
        var http = _httpFactory.CreateClient();
        var json = await http.GetStringAsync($"{_baseUrl}/podcasts");
        return JsonSerializer.Deserialize<List<PodcastMetadata>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    public async Task<PodcastMetadata?> GetByIdAsync(string id)
    {
        try
        {
            var http = _httpFactory.CreateClient();
            var json = await http.GetStringAsync($"{_baseUrl}/podcasts/{id}");
            return JsonSerializer.Deserialize<PodcastMetadata>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }
}

/// <summary>
/// Downloads/streams a podcast MP3 from the server.
/// The server handles all cache/retrieval logic transparently.
/// </summary>
public class StreamClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string _baseUrl;
    private readonly string _downloadDir;
    private readonly ILogger<StreamClient> _logger;

    public StreamClient(IHttpClientFactory httpFactory,
        IOptions<BrowserSettings> opts,
        ILogger<StreamClient> logger)
    {
        _httpFactory = httpFactory;
        _baseUrl = opts.Value.ServerUrl.TrimEnd('/');
        _downloadDir = opts.Value.DownloadDirectory;
        _logger = logger;
        Directory.CreateDirectory(_downloadDir);
    }

    /// <summary>
    /// Request a podcast stream and save it locally.
    /// In a real browser, this would pipe directly to an audio player.
    /// </summary>
    public async Task<string?> DownloadAsync(string podcastId, string fileName)
    {
        var http = _httpFactory.CreateClient();
        var url = $"{_baseUrl}/podcast/{podcastId}/stream";

        _logger.LogInformation("Requesting stream: {Url}", url);
        var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Stream failed: {Status}", response.StatusCode);
            return null;
        }

        var outputPath = Path.Combine(_downloadDir, fileName);
        await using var fileStream = File.Create(outputPath);
        await response.Content.CopyToAsync(fileStream);

        _logger.LogInformation("Saved to: {Path}", outputPath);
        return outputPath;
    }
}
