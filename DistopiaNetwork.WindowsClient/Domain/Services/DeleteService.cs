using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.WindowsClient.Api;
using DistopiaNetwork.WindowsClient.Data;

namespace DistopiaNetwork.WindowsClient.Domain.Services;

public class DeleteService
{
    private readonly PodcastApiClient _api;
    private readonly ILocalEpisodeRepository _repo;

    public DeleteService(PodcastApiClient api, ILocalEpisodeRepository repo)
    {
        _api = api;
        _repo = repo;
    }

    public async Task<OperationResponse> DeleteAsync(string podcastId, string? reason, CancellationToken ct = default)
    {
        var result = await _api.DeletePodcastAsync(podcastId, reason, ct);
        if (!result.Success)
            return result;

        await _repo.RemoveByIdAsync(podcastId, ct);
        return result;
    }
}
