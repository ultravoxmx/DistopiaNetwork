using DistopiaNetwork.PublisherClient.Configuration;
using DistopiaNetwork.Shared.Crypto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.PublisherClient.Services;

/// <summary>
/// Manages the publisher RSA key pair.
/// Keys are persisted to disk. If missing, a new pair is generated.
/// Multiple backup clients share the same key pair (Section 12).
/// </summary>
public class KeyStore
{
    public string PublicKey { get; private set; } = string.Empty;
    public string PrivateKey { get; private set; } = string.Empty;

    private readonly ILogger<KeyStore> _logger;

    public KeyStore(IOptions<PublisherSettings> opts, ILogger<KeyStore> logger)
    {
        _logger = logger;
        var pubPath = opts.Value.PublicKeyPath;
        var privPath = opts.Value.PrivateKeyPath;

        if (File.Exists(pubPath) && File.Exists(privPath))
        {
            PublicKey = File.ReadAllText(pubPath).Trim();
            PrivateKey = File.ReadAllText(privPath).Trim();
            _logger.LogInformation("Loaded existing key pair from disk.");
        }
        else
        {
            _logger.LogInformation("No key pair found. Generating new RSA-2048 key pair...");
            (PublicKey, PrivateKey) = CryptoHelper.GenerateKeyPair();
            File.WriteAllText(pubPath, PublicKey);
            File.WriteAllText(privPath, PrivateKey);
            _logger.LogInformation("Key pair saved to {Pub} / {Priv}", pubPath, privPath);
        }
    }
}
