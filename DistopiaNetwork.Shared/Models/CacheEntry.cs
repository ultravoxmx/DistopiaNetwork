namespace DistopiaNetwork.Shared.Models;

/// <summary>
/// Represents a cached MP3 file entry on a server.
/// Cache lifetime: minimum 1 day, maximum 7 days.
/// Each access resets the expiration timer.
/// </summary>
public class CacheEntry
{
    public string FileHash { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    public DateTime ExpiryTimestamp { get; set; }

    public static readonly TimeSpan MinTtl = TimeSpan.FromDays(1);
    public static readonly TimeSpan MaxTtl = TimeSpan.FromDays(7);

    public bool IsExpired => DateTime.UtcNow > ExpiryTimestamp;

    public void ResetExpiry(TimeSpan? ttl = null)
    {
        LastAccess = DateTime.UtcNow;
        ExpiryTimestamp = DateTime.UtcNow + (ttl ?? MaxTtl);
    }
}
