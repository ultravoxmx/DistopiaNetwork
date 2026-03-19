using DistopiaNetwork.Shared.Crypto;
using DistopiaNetwork.Shared.Models;

namespace DistopiaNetwork.WindowsClient.Security;

public class RequestSigner
{
    private readonly KeyStore _keys;

    public RequestSigner(KeyStore keys)
    {
        _keys = keys;
    }

    public string SignActionPayload(string payload)
        => CryptoHelper.SignPayload(payload, _keys.PrivateKey);

    public string SignMetadata(PodcastMetadata metadata)
        => CryptoHelper.SignMetadata(metadata, _keys.PrivateKey);
}
