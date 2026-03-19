using DistopiaNetwork.WindowsClient.Data;
using DistopiaNetwork.WindowsClient.Domain.Models;

namespace DistopiaNetwork.WindowsClient.Domain.Services;

public class CatalogService
{
    private readonly ILocalEpisodeRepository _repo;

    public CatalogService(ILocalEpisodeRepository repo)
    {
        _repo = repo;
    }

    public async Task<List<PodcastItemView>> LoadAsync(CancellationToken ct = default)
    {
        var localEpisodes = await _repo.GetAllAsync(ct);
        return localEpisodes
            .Select(ToView)
            .OrderByDescending(p => p.PublishTimestamp)
            .ToList();
    }

    private static PodcastItemView ToView(LocalEpisodeEntity p)
    {
        return new PodcastItemView
        {
            PodcastId = p.PodcastId,
            Title = p.Title,
            PublisherServer = "local",
            PublishTimestamp = p.PublishTimestamp,
            DurationSeconds = p.DurationSeconds,
            FileSize = p.FileSize,
            FileHash = p.FileHash
        };
    }
}
