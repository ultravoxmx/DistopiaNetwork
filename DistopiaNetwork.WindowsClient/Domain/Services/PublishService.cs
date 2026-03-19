using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.WindowsClient.Api;
using DistopiaNetwork.WindowsClient.Data;

namespace DistopiaNetwork.WindowsClient.Domain.Services;

public class PublishService
{
    private readonly PodcastApiClient _api;
    private readonly ILocalEpisodeRepository _repo;

    public PublishService(PodcastApiClient api, ILocalEpisodeRepository repo)
    {
        _api = api;
        _repo = repo;
    }

    public async Task<OperationResponse> PublishAsync(
        string filePath,
        string title,
        string description,
        int durationSeconds,
        string? imageUrl,
        CancellationToken ct = default)
    {
        var result = await _api.PublishAsync(filePath, title, description, durationSeconds, imageUrl, ct);
        if (!result.Success)
            return result;

        await _repo.AddOrUpdateAsync(new LocalEpisodeEntity
        {
            PodcastId = result.PodcastId,
            Title = title,
            Description = description,
            FileHash = result.FileHash,
            LocalFilePath = Path.GetFullPath(filePath),
            FileSize = result.FileSize,
            DurationSeconds = durationSeconds,
            PublishTimestamp = result.PublishTimestamp,
            UploadStatus = UploadStatuses.FullyUploaded,
            CreatedAt = DateTime.UtcNow,
            LastAttemptAt = DateTime.UtcNow,
        }, ct);

        return result;
    }
}
