using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DistopiaNetwork.PublisherClient.Configuration;
using DistopiaNetwork.PublisherClient.Data.Repositories;
using DistopiaNetwork.PublisherClient.Entities;
using DistopiaNetwork.Shared.Crypto;
using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.Shared.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.PublisherClient.Services;

/// <summary>
/// Gestisce il workflow completo di pubblicazione di un episodio.
/// 
/// Flusso con DAL:
///   1. Leggi file MP3 e calcola SHA-256
///   2. Controlla duplicati nel DB locale (offline, zero rete)
///   3. Costruisci e firma i metadati RSA
///   4. Salva episodio come Draft nel DB SQLite locale
///   5. POST metadati al server → aggiorna stato a MetadataPublished
///   6. Upload MP3 → aggiorna stato a FullyUploaded (o Failed)
/// 
/// In caso di interruzione tra i passi 5 e 6, al prossimo avvio
/// GetPendingAsync() individua gli episodi in stato Draft/Failed
/// e può riprendere l'upload.
/// </summary>
public class PublishService
{
    private readonly ILocalEpisodeRepository _repo;
    private readonly KeyStore _keyStore;
    private readonly IHttpClientFactory _httpFactory;
    private readonly PublisherSettings _settings;
    private readonly ILogger<PublishService> _logger;

    public PublishService(
        ILocalEpisodeRepository repo,
        KeyStore keyStore,
        IHttpClientFactory httpFactory,
        IOptions<PublisherSettings> settings,
        ILogger<PublishService> logger)
    {
        _repo = repo;
        _keyStore = keyStore;
        _httpFactory = httpFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Pubblica un nuovo episodio.
    /// Ritorna true se l'upload è completato con successo, false altrimenti.
    /// </summary>
    public async Task<bool> PublishAsync(
        string filePath,
        string title,
        string description,
        int durationSeconds,
        string? imageUrl = null,
        CancellationToken ct = default)
    {
        // ── Passo 1: Leggi il file e calcola l'hash ───────────────────────────
        if (!File.Exists(filePath))
        {
            _logger.LogError("File not found: {Path}", filePath);
            return false;
        }

        var data = await File.ReadAllBytesAsync(filePath, ct);
        var fileHash = CryptoHelper.ComputeFileHash(data);

        _logger.LogInformation("File hash: {Hash}", fileHash);

        // ── Passo 2: Controlla duplicati nel DB locale ────────────────────────
        var existingLocal = await _repo.FindByHashAsync(fileHash);
        if (existingLocal is not null)
        {
            _logger.LogWarning("Duplicate detected locally: '{Title}' (Status: {Status})",
                existingLocal.Title, existingLocal.UploadStatus);

            // Se era Failed, offri di riprovare
            if (existingLocal.UploadStatus == UploadStatuses.Failed)
            {
                _logger.LogInformation("Previous upload failed. Retrying...");
                return await RetryUploadAsync(existingLocal, data, ct);
            }

            Console.WriteLine($"⚠ Già pubblicato come '{existingLocal.Title}' (stato: {existingLocal.UploadStatus})");
            return false;
        }

        // ── Passo 3: Costruisci e firma i metadati ────────────────────────────
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var podcastId = Guid.NewGuid().ToString();

        var metadata = new PodcastMetadata
        {
            PodcastId       = podcastId,
            PublisherPubKey = _keyStore.PublicKey,
            PublisherServer = _settings.ServerId,
            Title           = title,
            Description     = description,
            ImageUrl        = imageUrl,
            FileHash        = fileHash,
            FileSize        = data.Length,
            DurationSeconds = durationSeconds,
            PublishTimestamp = timestamp,
            Signature       = string.Empty   // verrà popolata sotto
        };

        metadata.Signature = CryptoHelper.SignMetadata(metadata, _keyStore.PrivateKey);

        // ── Passo 4: Salva come Draft nel DB locale ───────────────────────────
        var episode = new LocalEpisodeEntity
        {
            PodcastId        = podcastId,
            Title            = title,
            Description      = description,
            FileHash         = fileHash,
            LocalFilePath    = Path.GetFullPath(filePath),
            FileSize         = data.Length,
            DurationSeconds  = durationSeconds,
            PublishTimestamp = timestamp,
            UploadStatus     = UploadStatuses.Draft,
            CreatedAt        = DateTime.UtcNow,
        };

        await _repo.AddAsync(episode);
        await _repo.SaveChangesAsync(ct);
        _logger.LogInformation("Episode saved as Draft: {Id}", podcastId);

        // ── Passo 5: POST metadati al server ──────────────────────────────────
        var http = _httpFactory.CreateClient();
        PublishResponse? publishResult;

        try
        {
            var metaResponse = await http.PostAsJsonAsync(
                $"{_settings.ServerUrl}/podcast/publish", metadata, ct);

            if (metaResponse.StatusCode == HttpStatusCode.Conflict)
            {
                var body = await metaResponse.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Server rejected (duplicate): {Body}", body);
                await UpdateStatusAsync(episode, UploadStatuses.Failed, $"Conflict: {body}", ct);
                return false;
            }

            if (!metaResponse.IsSuccessStatusCode)
            {
                var err = await metaResponse.Content.ReadAsStringAsync(ct);
                await UpdateStatusAsync(episode, UploadStatuses.Failed, $"HTTP {(int)metaResponse.StatusCode}: {err}", ct);
                return false;
            }

            publishResult = await metaResponse.Content.ReadFromJsonAsync<PublishResponse>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network error during metadata POST.");
            await UpdateStatusAsync(episode, UploadStatuses.Failed, ex.Message, ct);
            return false;
        }

        await UpdateStatusAsync(episode, UploadStatuses.MetadataPublished, null, ct);
        _logger.LogInformation("Metadata published. Upload URL: {Url}", publishResult?.UploadUrl);

        // ── Passo 6: Upload MP3 ───────────────────────────────────────────────
        return await UploadMp3Async(episode, data, publishResult?.UploadUrl, ct);
    }

    /// <summary>
    /// Riprova l'upload di un episodio precedentemente fallito.
    /// </summary>
    public async Task<bool> RetryUploadAsync(LocalEpisodeEntity episode, byte[]? data = null, CancellationToken ct = default)
    {
        data ??= await File.ReadAllBytesAsync(episode.LocalFilePath, ct);
        var uploadUrl = $"/podcast/{episode.PodcastId}/upload";
        return await UploadMp3Async(episode, data, uploadUrl, ct);
    }

    /// <summary>
    /// Elenca gli episodi in stato Draft o Failed per riprendere upload interrotti.
    /// </summary>
    public async Task<IEnumerable<LocalEpisodeEntity>> GetPendingEpisodesAsync()
        => await _repo.GetPendingAsync();

    /// <summary>
    /// Elenca tutti gli episodi pubblicati con successo.
    /// </summary>
    public async Task<IEnumerable<LocalEpisodeEntity>> GetPublishedEpisodesAsync()
        => await _repo.GetAllByStatusAsync(UploadStatuses.FullyUploaded);

    // ── Helpers privati ───────────────────────────────────────────────────────

    private async Task<bool> UploadMp3Async(
        LocalEpisodeEntity episode, byte[] data, string? uploadPath, CancellationToken ct)
    {
        var url = $"{_settings.ServerUrl}{uploadPath ?? $"/podcast/{episode.PodcastId}/upload"}";

        try
        {
            var http = _httpFactory.CreateClient();
            using var content = new ByteArrayContent(data);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");

            var response = await http.PostAsync(url, content, ct);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                await UpdateStatusAsync(episode, UploadStatuses.Failed, $"Upload HTTP {(int)response.StatusCode}: {err}", ct);
                return false;
            }

            await UpdateStatusAsync(episode, UploadStatuses.FullyUploaded, null, ct);
            _logger.LogInformation("✓ Episode '{Title}' fully uploaded.", episode.Title);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network error during MP3 upload.");
            await UpdateStatusAsync(episode, UploadStatuses.Failed, ex.Message, ct);
            return false;
        }
    }

    private async Task UpdateStatusAsync(
        LocalEpisodeEntity episode, string status, string? errorMessage, CancellationToken ct)
    {
        // Ricarica l'entità con tracking per aggiornare
        var tracked = await _repo.GetByIdAsync(episode.PodcastId);
        if (tracked is null) return;

        tracked.UploadStatus     = status;
        tracked.LastAttemptAt    = DateTime.UtcNow;
        tracked.LastErrorMessage = errorMessage;

        _repo.Update(tracked);
        await _repo.SaveChangesAsync(ct);
    }
}
