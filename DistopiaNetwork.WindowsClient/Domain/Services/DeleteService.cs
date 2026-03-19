using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.WindowsClient.Api;

namespace DistopiaNetwork.WindowsClient.Domain.Services;

public class DeleteService
{
    private readonly PodcastApiClient _api;

    public DeleteService(PodcastApiClient api)
    {
        _api = api;
    }

    public Task<OperationResponse> DeleteAsync(string podcastId, string? reason, CancellationToken ct = default)
        => _api.DeletePodcastAsync(podcastId, reason, ct);
}
