namespace DistopiaNetwork.WindowsClient.Data;

public interface ILocalEpisodeRepository
{
    Task<List<LocalEpisodeEntity>> GetAllAsync(CancellationToken ct = default);
    Task<LocalEpisodeEntity?> GetByIdAsync(string podcastId, CancellationToken ct = default);
    Task AddOrUpdateAsync(LocalEpisodeEntity entity, CancellationToken ct = default);
    Task RemoveByIdAsync(string podcastId, CancellationToken ct = default);
}
