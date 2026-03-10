using System;
using System.ComponentModel.DataAnnotations;

namespace DistopiaNetwork.PublisherClient.Entities;

/// <summary>
/// Rappresentazione persistente di un episodio pubblicato dal creator.
/// Salvato nel database SQLite locale del publisher client.
/// 
/// Traccia l'intero ciclo di vita dell'upload con UploadStatus:
///   Draft            → metadati costruiti, nessuna comunicazione di rete ancora
///   MetadataPublished → POST /podcast/publish completato con successo
///   FullyUploaded    → POST /podcast/{id}/upload completato con successo  
///   Failed           → uno degli step precedenti ha fallito (vedi LastErrorMessage)
/// </summary>
public class LocalEpisodeEntity
{
    [Key]
    [MaxLength(36)]
    public string PodcastId { get; set; } = default!;

    [Required]
    [MaxLength(512)]
    public string Title { get; set; } = default!;

    [MaxLength(4096)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    public string FileHash { get; set; } = default!;    // SHA-256, indice UNIQUE

    [Required]
    [MaxLength(2048)]
    public string LocalFilePath { get; set; } = default!; // path sul disco del creator

    public long FileSize { get; set; }

    public int DurationSeconds { get; set; }

    public long PublishTimestamp { get; set; }

    /// <summary>
    /// Stato corrente dell'upload: Draft | MetadataPublished | FullyUploaded | Failed
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string UploadStatus { get; set; } = UploadStatuses.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastAttemptAt { get; set; }

    /// <summary>
    /// Messaggio di errore dell'ultimo tentativo fallito. Null se l'ultimo tentativo ha avuto successo.
    /// </summary>
    [MaxLength(2048)]
    public string? LastErrorMessage { get; set; }
}

/// <summary>
/// Costanti per i valori validi di UploadStatus.
/// </summary>
public static class UploadStatuses
{
    public const string Draft             = "Draft";
    public const string MetadataPublished = "MetadataPublished";
    public const string FullyUploaded    = "FullyUploaded";
    public const string Failed            = "Failed";
}
