using DistopiaNetwork.PublisherClient.Configuration;
using DistopiaNetwork.Shared.Crypto;
using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace DistopiaNetwork.PublisherClient.Services;

/// <summary>
/// Handles the full podcast publication workflow:
/// 1. Compute SHA-256 of the MP3
/// 2. Build + sign metadata with the private key
/// 3. POST metadata to server  — server rejects immediately if duplicate
/// 4. Upload MP3 only if server confirms it is needed
/// </summary>
public class PublishService
{
    private readonly KeyStore _keys;
    private readonly PublisherSettings _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PublishService> _logger;

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true   // handles both camelCase and PascalCase responses
    };

    public PublishService(
        KeyStore keys,
        IOptions<PublisherSettings> opts,
        IHttpClientFactory httpFactory,
        ILogger<PublishService> logger)
    {
        _keys = keys;
        _settings = opts.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<bool> PublishAsync(
        string mp3Path,
        string title,
        string description,
        int durationSeconds,
        string? imageUrl = null)
    {
        if (!_settings.IsActive)
        {
            _logger.LogWarning("This client is a BACKUP and is not active. Aborting publish.");
            return false;
        }

        // ── Read file ────────────────────────────────────────────────────────────
        if (!File.Exists(mp3Path))
        {
            _logger.LogError("File not found: {Path}", mp3Path);
            return false;
        }

        _logger.LogInformation("Reading MP3: {Path}", mp3Path);
        byte[] mp3Data;
        try
        {
            mp3Data = await File.ReadAllBytesAsync(mp3Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file: {Path}", mp3Path);
            return false;
        }

        var fileHash = CryptoHelper.ComputeFileHash(mp3Data);
        _logger.LogInformation("File hash: {Hash}", fileHash);

        // ── Build + sign metadata ─────────────────────────────────────────────────
        var metadata = new PodcastMetadata
        {
            PodcastId        = Guid.NewGuid().ToString(),
            PublisherPubKey  = _keys.PublicKey,
            PublisherServer  = _settings.ServerId,
            Title            = title,
            Description      = description,
            ImageUrl         = string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl,
            FileHash         = fileHash,
            FileSize         = mp3Data.Length,
            DurationSeconds  = durationSeconds,
            PublishTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        try
        {
            metadata.Signature = CryptoHelper.SignMetadata(metadata, _keys.PrivateKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sign metadata. Check your private key file.");
            return false;
        }

        _logger.LogInformation("Metadata signed. PodcastId: {Id}", metadata.PodcastId);

        var http    = _httpFactory.CreateClient();
        var baseUrl = _settings.ServerUrl.TrimEnd('/');

        // ── Step 1: POST metadata ─────────────────────────────────────────────────
        string uploadUrl;
        try
        {
            var metaJson    = JsonSerializer.Serialize(metadata);
            var metaContent = new StringContent(metaJson, System.Text.Encoding.UTF8, "application/json");

            _logger.LogInformation("POST {Url}/podcast/publish", baseUrl);
            var metaResponse = await http.PostAsync($"{baseUrl}/podcast/publish", metaContent);

            // 409 Conflict = server already has this exact file
            if (metaResponse.StatusCode == HttpStatusCode.Conflict)
            {
                var body = await metaResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Duplicate detected by server: {Body}", body);
                Console.WriteLine($"\n⚠  Duplicate — {body}");
                return false;
            }

            if (!metaResponse.IsSuccessStatusCode)
            {
                var err = await metaResponse.Content.ReadAsStringAsync();
                _logger.LogError("Metadata publish failed [{Status}]: {Error}",
                    (int)metaResponse.StatusCode, err);
                return false;
            }

            var publishJson   = await metaResponse.Content.ReadAsStringAsync();
            _logger.LogDebug("Server response: {Json}", publishJson);

            // Deserialize with case-insensitive matching to handle both
            // camelCase ("uploadUrl") and PascalCase ("UploadUrl") responses.
            var publishResult = JsonSerializer.Deserialize<PublishResponse>(publishJson, _jsonOpts);

            uploadUrl = publishResult?.UploadUrl ?? string.Empty;
            if (string.IsNullOrEmpty(uploadUrl))
            {
                _logger.LogError("Server returned success but no uploadUrl. Response: {Json}", publishJson);
                return false;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Network error posting metadata to {Url}. " +
                "Is the server running? Check ServerUrl in appsettings.json.",
                baseUrl);
            Console.WriteLine($"\n✗  Cannot reach server at {baseUrl}");
            Console.WriteLine($"   {ex.Message}");
            if (ex.InnerException is not null)
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            return false;
        }

        // ── Step 2: Upload MP3 ────────────────────────────────────────────────────
        try
        {
            _logger.LogInformation("Uploading MP3 ({Size:N0} bytes) → {Url}", mp3Data.Length, uploadUrl);
            var mp3Content = new ByteArrayContent(mp3Data);
            mp3Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");

            var uploadResponse = await http.PostAsync($"{baseUrl}{uploadUrl}", mp3Content);

            if (!uploadResponse.IsSuccessStatusCode)
            {
                var err = await uploadResponse.Content.ReadAsStringAsync();
                _logger.LogError("MP3 upload failed [{Status}]: {Error}",
                    (int)uploadResponse.StatusCode, err);
                return false;
            }

            var uploadMsg = await uploadResponse.Content.ReadAsStringAsync();
            _logger.LogInformation("Upload complete. Server says: {Msg}", uploadMsg);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error uploading MP3 to {Url}.", uploadUrl);
            return false;
        }

        Console.WriteLine($"\n✓  Published — ID: {metadata.PodcastId}");
        return true;
    }
}
