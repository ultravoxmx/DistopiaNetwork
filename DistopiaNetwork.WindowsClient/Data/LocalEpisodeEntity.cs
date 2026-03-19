using System.ComponentModel.DataAnnotations;

namespace DistopiaNetwork.WindowsClient.Data;

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
    public string FileHash { get; set; } = default!;

    [Required]
    [MaxLength(2048)]
    public string LocalFilePath { get; set; } = default!;

    public long FileSize { get; set; }

    public int DurationSeconds { get; set; }

    public long PublishTimestamp { get; set; }

    [Required]
    [MaxLength(32)]
    public string UploadStatus { get; set; } = UploadStatuses.FullyUploaded;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptAt { get; set; }

    [MaxLength(2048)]
    public string? LastErrorMessage { get; set; }
}

public static class UploadStatuses
{
    public const string FullyUploaded = "FullyUploaded";
    public const string Failed = "Failed";
}
