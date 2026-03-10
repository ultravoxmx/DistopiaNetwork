using System.Text.Json.Serialization;

namespace DistopiaNetwork.Shared.Models;

/// <summary>
/// Represents a podcast episode's metadata, synchronized across all servers.
/// </summary>
public class PodcastMetadata
{
    [JsonPropertyName("podcast_id")]
    public string PodcastId { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("publisher_pubkey")]
    public string PublisherPubKey { get; set; } = string.Empty;

    [JsonPropertyName("publisher_server")]
    public string PublisherServer { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("file_hash")]
    public string FileHash { get; set; } = string.Empty;

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }

    [JsonPropertyName("duration_seconds")]
    public int DurationSeconds { get; set; }

    [JsonPropertyName("publish_timestamp")]
    public long PublishTimestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;
}
