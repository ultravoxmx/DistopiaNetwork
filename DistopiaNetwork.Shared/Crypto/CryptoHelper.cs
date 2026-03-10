using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DistopiaNetwork.Shared.Crypto;

/// <summary>
/// Handles RSA digital signatures for podcast metadata integrity.
/// Publisher signs metadata with private key; servers verify with public key.
/// </summary>
public static class CryptoHelper
{
    /// <summary>Generate a new RSA key pair. Returns (publicKeyPem, privateKeyPem).</summary>
    public static (string PublicKey, string PrivateKey) GenerateKeyPair()
    {
        using var rsa = RSA.Create(2048);
        var pubKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var privKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        return (pubKey, privKey);
    }

    /// <summary>
    /// Sign metadata: serializes all fields except 'signature', then signs SHA256 hash.
    /// </summary>
    public static string SignMetadata(Models.PodcastMetadata metadata, string privateKeyBase64)
    {
        var payload = GetSignablePayload(metadata);
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var signature = rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(signature);
    }

    /// <summary>
    /// Verify metadata signature using the publisher's public key.
    /// </summary>
    public static bool VerifyMetadata(Models.PodcastMetadata metadata, string publicKeyBase64)
    {
        try
        {
            var payload = GetSignablePayload(metadata);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            var signature = Convert.FromBase64String(metadata.Signature);
            return rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Compute SHA256 of a file's bytes.</summary>
    public static string ComputeFileHash(byte[] data)
        => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>Verify that file bytes match the expected hash in metadata.</summary>
    public static bool VerifyFileHash(byte[] data, string expectedHash)
        => ComputeFileHash(data).Equals(expectedHash, StringComparison.OrdinalIgnoreCase);

    private static string GetSignablePayload(Models.PodcastMetadata m)
    {
        // Serialize without the signature field for signing
        var payload = new
        {
            m.PodcastId,
            m.PublisherPubKey,
            m.PublisherServer,
            m.Title,
            m.Description,
            m.ImageUrl,
            m.FileHash,
            m.FileSize,
            m.DurationSeconds,
            m.PublishTimestamp
        };
        return JsonSerializer.Serialize(payload);
    }
}
