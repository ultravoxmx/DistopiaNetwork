using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.WindowsClient.Api;

namespace DistopiaNetwork.WindowsClient.Domain.Services;

public class MetadataEditService
{
    private readonly PodcastApiClient _api;

    public MetadataEditService(PodcastApiClient api)
    {
        _api = api;
    }

    public Task<OperationResponse> UpdateAsync(string podcastId, string title, string description, string? imageUrl, CancellationToken ct = default)
        => _api.UpdateMetadataAsync(podcastId, title, description, imageUrl, ct);
}
