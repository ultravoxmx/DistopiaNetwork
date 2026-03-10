using System;
using System.ComponentModel.DataAnnotations;

namespace DistopiaNetwork.Server.Entities;

/// <summary>
/// Rappresentazione persistente di un episodio podcast nel database SQL Server.
/// Distinta da PodcastMetadata (contratto di rete) per separare persistenza e protocollo.
/// </summary>
public class PodcastEntity
{
    [Key]
    [MaxLength(36)]
    public string PodcastId { get; set; } = default!;

    [Required]
    [MaxLength(4096)]
    public string PublisherPubKey { get; set; } = default!;

    [Required]
    [MaxLength(64)]
    public string PublisherServer { get; set; } = default!;

    [Required]
    [MaxLength(512)]
    public string Title { get; set; } = default!;

    [MaxLength(4096)]
    public string Description { get; set; } = string.Empty;

    [MaxLength(2048)]
    public string? ImageUrl { get; set; }

    [Required]
    [MaxLength(64)]
    public string FileHash { get; set; } = default!;   // SHA-256 lowercase hex

    public long FileSize { get; set; }

    public int DurationSeconds { get; set; }

    public long PublishTimestamp { get; set; }         // Unix timestamp UTC

    [Required]
    public string Signature { get; set; } = default!;  // Base64 RSA signature (nvarchar MAX)

    // Colonne di audit — non fanno parte del protocollo di rete
    public DateTime InsertedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
