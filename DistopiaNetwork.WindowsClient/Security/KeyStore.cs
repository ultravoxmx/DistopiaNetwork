using DistopiaNetwork.Shared.Crypto;
using DistopiaNetwork.WindowsClient.Configuration;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.WindowsClient.Security;

public class KeyStore
{
    public string PublicKey { get; }
    public string PrivateKey { get; }

    public KeyStore(IOptions<WindowsClientSettings> settings)
    {
        var pubPath = settings.Value.PublicKeyPath;
        var privPath = settings.Value.PrivateKeyPath;

        if (File.Exists(pubPath) && File.Exists(privPath))
        {
            PublicKey = File.ReadAllText(pubPath).Trim();
            PrivateKey = File.ReadAllText(privPath).Trim();
            return;
        }

        (PublicKey, PrivateKey) = CryptoHelper.GenerateKeyPair();
        File.WriteAllText(pubPath, PublicKey);
        File.WriteAllText(privPath, PrivateKey);
    }
}
