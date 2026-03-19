using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.WindowsClient.Api;

namespace DistopiaNetwork.WindowsClient.Domain.Services;

public class PublishService
{
    private readonly PodcastApiClient _api;

    public PublishService(PodcastApiClient api)
    {
        _api = api;
    }

    public Task<OperationResponse> PublishAsync(
        string filePath,
        string title,
        string description,
        int durationSeconds,
        string? imageUrl,
        CancellationToken ct = default)
        => _api.PublishAsync(filePath, title, description, durationSeconds, imageUrl, ct);
}
