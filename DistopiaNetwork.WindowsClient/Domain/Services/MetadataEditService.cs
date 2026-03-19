using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.WindowsClient.Api;
using DistopiaNetwork.WindowsClient.Data;

namespace DistopiaNetwork.WindowsClient.Domain.Services;

public class MetadataEditService
{
    private readonly PodcastApiClient _api;
    private readonly ILocalEpisodeRepository _repo;

    public MetadataEditService(PodcastApiClient api, ILocalEpisodeRepository repo)
    {
        _api = api;
        _repo = repo;
    }

    public async Task<OperationResponse> UpdateAsync(string podcastId, string title, string description, string? imageUrl, CancellationToken ct = default)
    {
        var result = await _api.UpdateMetadataAsync(podcastId, title, description, imageUrl, ct);
        if (!result.Success)
            return result;

        var local = await _repo.GetByIdAsync(podcastId, ct);
        if (local is not null)
        {
            local.Title = title;
            local.Description = description;
            local.LastAttemptAt = DateTime.UtcNow;
            local.LastErrorMessage = null;
            await _repo.AddOrUpdateAsync(local, ct);
        }

        return result;
    }
}
