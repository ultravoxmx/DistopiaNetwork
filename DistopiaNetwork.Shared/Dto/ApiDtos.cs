using DistopiaNetwork.Shared.Models;

namespace DistopiaNetwork.Shared.Dto;

/// <summary>Request to publish a new podcast episode.</summary>
public class PublishRequest
{
    public PodcastMetadata Metadata { get; set; } = new();
}

/// <summary>Response from a publish request.</summary>
public class PublishResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? UploadUrl { get; set; }
}

/// <summary>Response for catalog synchronization: returns all podcasts since a timestamp.</summary>
public class SyncResponse
{
    public List<PodcastMetadata> Podcasts { get; set; } = new();
}

/// <summary>Request from one server to another for an MP3 file.</summary>
public class FileRequest
{
    public string FileHash { get; set; } = string.Empty;
    public string RequestingServerId { get; set; } = string.Empty;
}

public class UpdateMetadataRequest
{
    public string PodcastId { get; set; } = string.Empty;
    public string PublisherPubKey { get; set; } = string.Empty;
    public long TimestampUnix { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Signature { get; set; } = string.Empty;
    public string MetadataSignature { get; set; } = string.Empty;
}

public class DeletePodcastRequest
{
    public string PodcastId { get; set; } = string.Empty;
    public string PublisherPubKey { get; set; } = string.Empty;
    public long TimestampUnix { get; set; }
    public string Nonce { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string Signature { get; set; } = string.Empty;
    public string MetadataSignature { get; set; } = string.Empty;
}

public class OperationResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long ServerTimestamp { get; set; }
}
