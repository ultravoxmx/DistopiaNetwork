using DistopiaNetwork.Shared.Models;
using DistopiaNetwork.WindowsClient.Api;
using DistopiaNetwork.WindowsClient.Domain.Models;
using DistopiaNetwork.WindowsClient.Security;

namespace DistopiaNetwork.WindowsClient.Domain.Services;

public class CatalogService
{
    private readonly PodcastApiClient _api;
    private readonly KeyStore _keys;

    public CatalogService(PodcastApiClient api, KeyStore keys)
    {
        _api = api;
        _keys = keys;
    }

    public async Task<List<PodcastItemView>> LoadAsync(CancellationToken ct = default)
    {
        var catalog = await _api.GetCatalogAsync(ct);
        return catalog.Select(ToView).OrderByDescending(p => p.PublishTimestamp).ToList();
    }

    private PodcastItemView ToView(PodcastMetadata p)
    {
        var keyShort = p.PublisherPubKey.Length > 16 ? p.PublisherPubKey[..16] + "..." : p.PublisherPubKey;
        return new PodcastItemView
        {
            PodcastId = p.PodcastId,
            Title = p.Title,
            PublisherServer = p.PublisherServer,
            PublisherPubKeyShort = keyShort,
            PublishTimestamp = p.PublishTimestamp,
            DurationSeconds = p.DurationSeconds,
            FileSize = p.FileSize,
            FileHash = p.FileHash,
            IsOwner = string.Equals(p.PublisherPubKey, _keys.PublicKey, StringComparison.Ordinal)
        };
    }
}
