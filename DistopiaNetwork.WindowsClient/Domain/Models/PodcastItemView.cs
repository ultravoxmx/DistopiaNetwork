namespace DistopiaNetwork.WindowsClient.Domain.Models;

public class PodcastItemView
{
    public string PodcastId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PublisherServer { get; set; } = string.Empty;
    public long PublishTimestamp { get; set; }
    public int DurationSeconds { get; set; }
    public long FileSize { get; set; }
    public string FileHash { get; set; } = string.Empty;
}
