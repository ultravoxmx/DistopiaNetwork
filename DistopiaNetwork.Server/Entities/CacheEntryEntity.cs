using System;
using System.ComponentModel.DataAnnotations;

namespace DistopiaNetwork.Server.Entities;

/// <summary>
/// Rappresentazione persistente di una voce nella cache MP3.
/// Tiene traccia del file fisico su disco e della sua scadenza TTL.
/// </summary>
public class CacheEntryEntity
{
    [Key]
    [MaxLength(64)]
    public string FileHash { get; set; } = default!;   // SHA-256, chiave naturale

    [Required]
    [MaxLength(1024)]
    public string FilePath { get; set; } = default!;   // path assoluto su disco

    public DateTime LastAccess { get; set; } = DateTime.UtcNow;

    public DateTime ExpiryTimestamp { get; set; }

    // FK opzionale verso Podcasts (SetNull se il podcast viene eliminato)
    [MaxLength(36)]
    public string? PodcastId { get; set; }

    public PodcastEntity? Podcast { get; set; }
}
