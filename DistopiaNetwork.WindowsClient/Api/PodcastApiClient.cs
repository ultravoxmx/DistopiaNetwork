using System.Net.Http.Json;
using DistopiaNetwork.Shared.Crypto;
using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.Shared.Models;
using DistopiaNetwork.WindowsClient.Configuration;
using DistopiaNetwork.WindowsClient.Security;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.WindowsClient.Api;

public class PodcastApiClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly WindowsClientSettings _settings;
    private readonly NonceProvider _nonce;
    private readonly RequestSigner _signer;
    private readonly KeyStore _keys;

    public PodcastApiClient(
        IHttpClientFactory httpFactory,
        IOptions<WindowsClientSettings> settings,
        NonceProvider nonce,
        RequestSigner signer,
        KeyStore keys)
    {
        _httpFactory = httpFactory;
        _settings = settings.Value;
        _nonce = nonce;
        _signer = signer;
        _keys = keys;
    }

    public async Task<List<PodcastMetadata>> GetCatalogAsync(CancellationToken ct = default)
    {
        var http = _httpFactory.CreateClient();
        var result = await http.GetFromJsonAsync<List<PodcastMetadata>>($"{_settings.ServerUrl}/podcasts", ct);
        return result ?? [];
    }

    public async Task<OperationResponse> UpdateMetadataAsync(string podcastId, string title, string description, string? imageUrl, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = _nonce.Next();

        var actionPayload = SignedPayloadBuilder.BuildUpdatePayload(
            podcastId,
            _keys.PublicKey,
            now,
            nonce,
            title,
            description,
            imageUrl);

        var metadata = await GetPodcastByIdAsync(podcastId, ct) ?? throw new InvalidOperationException("Podcast not found.");
        metadata.Title = title;
        metadata.Description = description;
        metadata.ImageUrl = imageUrl;
        metadata.IsDeleted = false;
        metadata.PublishTimestamp = now;

        var request = new UpdateMetadataRequest
        {
            PodcastId = podcastId,
            PublisherPubKey = _keys.PublicKey,
            TimestampUnix = now,
            Nonce = nonce,
            Title = title,
            Description = description,
            ImageUrl = imageUrl,
            Signature = _signer.SignActionPayload(actionPayload),
            MetadataSignature = _signer.SignMetadata(metadata)
        };

        var http = _httpFactory.CreateClient();
        var response = await http.PutAsJsonAsync($"{_settings.ServerUrl}/podcast/{podcastId}/metadata", request, ct);
        return await ReadOperationResponseAsync(response, ct);
    }

    public async Task<OperationResponse> DeletePodcastAsync(string podcastId, string? reason, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonce = _nonce.Next();

        var actionPayload = SignedPayloadBuilder.BuildDeletePayload(
            podcastId,
            _keys.PublicKey,
            now,
            nonce,
            reason);

        var metadata = await GetPodcastByIdAsync(podcastId, ct) ?? throw new InvalidOperationException("Podcast not found.");
        metadata.IsDeleted = true;
        metadata.PublishTimestamp = now;

        var request = new DeletePodcastRequest
        {
            PodcastId = podcastId,
            PublisherPubKey = _keys.PublicKey,
            TimestampUnix = now,
            Nonce = nonce,
            Reason = reason,
            Signature = _signer.SignActionPayload(actionPayload),
            MetadataSignature = _signer.SignMetadata(metadata)
        };

        var http = _httpFactory.CreateClient();
        var msg = new HttpRequestMessage(HttpMethod.Delete, $"{_settings.ServerUrl}/podcast/{podcastId}")
        {
            Content = JsonContent.Create(request)
        };

        var response = await http.SendAsync(msg, ct);
        return await ReadOperationResponseAsync(response, ct);
    }

    public async Task<OperationResponse> PublishAsync(
        string filePath,
        string title,
        string description,
        int durationSeconds,
        string? imageUrl,
        CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return new OperationResponse { Success = false, Error = $"File not found: {filePath}" };

        var data = await File.ReadAllBytesAsync(filePath, ct);
        var metadata = new PodcastMetadata
        {
            PodcastId = Guid.NewGuid().ToString(),
            PublisherPubKey = _keys.PublicKey,
            PublisherServer = _settings.ServerId,
            Title = title,
            Description = description,
            ImageUrl = imageUrl,
            FileHash = CryptoHelper.ComputeFileHash(data),
            FileSize = data.Length,
            DurationSeconds = durationSeconds,
            PublishTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            IsDeleted = false
        };

        metadata.Signature = _signer.SignMetadata(metadata);

        var http = _httpFactory.CreateClient();
        var publishResponse = await http.PostAsJsonAsync($"{_settings.ServerUrl}/podcast/publish", metadata, ct);

        if (!publishResponse.IsSuccessStatusCode)
        {
            var err = await publishResponse.Content.ReadAsStringAsync(ct);
            return new OperationResponse { Success = false, Error = $"Publish failed: HTTP {(int)publishResponse.StatusCode} {err}" };
        }

        var result = await publishResponse.Content.ReadFromJsonAsync<PublishResponse>(cancellationToken: ct);
        var uploadPath = result?.UploadUrl ?? $"/podcast/{metadata.PodcastId}/upload";

        using var content = new ByteArrayContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
        var upload = await http.PostAsync($"{_settings.ServerUrl}{uploadPath}", content, ct);
        if (!upload.IsSuccessStatusCode)
        {
            var err = await upload.Content.ReadAsStringAsync(ct);
            return new OperationResponse { Success = false, Error = $"Upload failed: HTTP {(int)upload.StatusCode} {err}" };
        }

        return new OperationResponse
        {
            Success = true,
            ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    private async Task<PodcastMetadata?> GetPodcastByIdAsync(string id, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        return await http.GetFromJsonAsync<PodcastMetadata>($"{_settings.ServerUrl}/podcasts/{id}", ct);
    }

    private static async Task<OperationResponse> ReadOperationResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var payload = await response.Content.ReadFromJsonAsync<OperationResponse>(cancellationToken: ct);
        if (payload is not null)
            return payload;

        return new OperationResponse
        {
            Success = response.IsSuccessStatusCode,
            Error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}",
            ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }
}
