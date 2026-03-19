using Microsoft.EntityFrameworkCore;

namespace DistopiaNetwork.WindowsClient.Data;

public class LocalEpisodeRepository : ILocalEpisodeRepository
{
    private readonly WindowsClientDbContext _db;

    public LocalEpisodeRepository(WindowsClientDbContext db)
    {
        _db = db;
    }

    public async Task<List<LocalEpisodeEntity>> GetAllAsync(CancellationToken ct = default)
        => await _db.Episodes.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct);

    public async Task<LocalEpisodeEntity?> GetByIdAsync(string podcastId, CancellationToken ct = default)
        => await _db.Episodes.FirstOrDefaultAsync(x => x.PodcastId == podcastId, ct);

    public async Task AddOrUpdateAsync(LocalEpisodeEntity entity, CancellationToken ct = default)
    {
        var existing = await _db.Episodes.FirstOrDefaultAsync(x => x.PodcastId == entity.PodcastId, ct);
        if (existing is null)
        {
            await _db.Episodes.AddAsync(entity, ct);
        }
        else
        {
            existing.Title = entity.Title;
            existing.Description = entity.Description;
            existing.FileHash = entity.FileHash;
            existing.LocalFilePath = entity.LocalFilePath;
            existing.FileSize = entity.FileSize;
            existing.DurationSeconds = entity.DurationSeconds;
            existing.PublishTimestamp = entity.PublishTimestamp;
            existing.UploadStatus = entity.UploadStatus;
            existing.LastAttemptAt = entity.LastAttemptAt;
            existing.LastErrorMessage = entity.LastErrorMessage;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveByIdAsync(string podcastId, CancellationToken ct = default)
    {
        var existing = await _db.Episodes.FirstOrDefaultAsync(x => x.PodcastId == podcastId, ct);
        if (existing is null) return;

        _db.Episodes.Remove(existing);
        await _db.SaveChangesAsync(ct);
    }
}
