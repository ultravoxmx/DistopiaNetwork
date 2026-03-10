using System.Collections.Concurrent;
using DistopiaNetwork.Shared.Crypto;
using DistopiaNetwork.Shared.Models;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Thread-safe in-memory podcast catalog.
/// Persists metadata (never MP3s).
/// </summary>
public class CatalogService
{
    private readonly ConcurrentDictionary<string, PodcastMetadata> _catalog = new();
    private readonly ILogger<CatalogService> _logger;

    public CatalogService(ILogger<CatalogService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Add or update a podcast entry after verifying its signature.
    /// Returns false if:
    ///   - the digital signature is invalid, OR
    ///   - the same file_hash is already registered by the same publisher
    ///     under a different podcast_id (duplicate MP3 prevention).
    /// </summary>
    public bool TryAddOrUpdate(PodcastMetadata metadata)
    {
        // 1. Verify cryptographic signature
        if (!CryptoHelper.VerifyMetadata(metadata, metadata.PublisherPubKey))
        {
            _logger.LogWarning("Rejected podcast {Id}: invalid signature.", metadata.PodcastId);
            return false;
        }

        // 2. Duplicate file detection: same SHA-256 + same publisher, different episode ID
        var duplicate = _catalog.Values.FirstOrDefault(p =>
            p.FileHash == metadata.FileHash &&
            p.PublisherPubKey == metadata.PublisherPubKey &&
            p.PodcastId != metadata.PodcastId);

        if (duplicate is not null)
        {
            _logger.LogWarning(
                "Rejected podcast {NewId} ({Title}): file_hash {Hash} already exists as episode {ExistingId} ({ExistingTitle}).",
                metadata.PodcastId, metadata.Title,
                metadata.FileHash.Substring(0, 12),
                duplicate.PodcastId, duplicate.Title);
            return false;
        }

        _catalog[metadata.PodcastId] = metadata;
        _logger.LogInformation("Catalog updated: {Id} - {Title}", metadata.PodcastId, metadata.Title);
        return true;
    }

    /// <summary>
    /// Sync variant: same duplicate check applies.
    /// Peers cannot push duplicate content either.
    /// </summary>
    public bool TryAddFromSync(PodcastMetadata metadata)
        => TryAddOrUpdate(metadata);

    public PodcastMetadata? Get(string podcastId)
        => _catalog.TryGetValue(podcastId, out var m) ? m : null;

    public IEnumerable<PodcastMetadata> GetAll()
        => _catalog.Values;

    /// <summary>Returns all podcasts published after the given Unix timestamp.</summary>
    public IEnumerable<PodcastMetadata> GetSince(long unixTimestamp)
        => _catalog.Values.Where(p => p.PublishTimestamp > unixTimestamp);

    /// <summary>
    /// Find any podcast that already references this file hash.
    /// Used by the upload endpoint to skip redundant MP3 transfers.
    /// </summary>
    public PodcastMetadata? FindByFileHash(string fileHash)
        => _catalog.Values.FirstOrDefault(p => p.FileHash == fileHash);

    public int Count => _catalog.Count;
}
