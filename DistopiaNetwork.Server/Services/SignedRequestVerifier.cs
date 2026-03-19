using System.Collections.Concurrent;
using System.Text;
using DistopiaNetwork.Shared.Crypto;

namespace DistopiaNetwork.Server.Services;

public class SignedRequestVerifier
{
    private static readonly TimeSpan AllowedSkew = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NonceTtl = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, DateTime> _seenNonces = new();

    public bool Verify(string publisherPubKey, long timestampUnix, string nonce, string payload, string signature)
    {
        if (string.IsNullOrWhiteSpace(publisherPubKey)
            || string.IsNullOrWhiteSpace(nonce)
            || string.IsNullOrWhiteSpace(signature))
            return false;

        var now = DateTimeOffset.UtcNow;
        var ts = DateTimeOffset.FromUnixTimeSeconds(timestampUnix);

        if ((now - ts).Duration() > AllowedSkew)
            return false;

        CleanupExpiredNonces();

        if (!_seenNonces.TryAdd(nonce, now.UtcDateTime + NonceTtl))
            return false;

        return CryptoHelper.VerifyPayload(payload, signature, publisherPubKey);
    }

    public static string BuildUpdatePayload(
        string podcastId,
        string publisherPubKey,
        long timestampUnix,
        string nonce,
        string title,
        string description,
        string? imageUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("action=update_metadata");
        sb.AppendLine($"podcast_id={podcastId}");
        sb.AppendLine($"publisher_pubkey={publisherPubKey}");
        sb.AppendLine($"timestamp_unix={timestampUnix}");
        sb.AppendLine($"nonce={nonce}");
        sb.AppendLine($"title={title}");
        sb.AppendLine($"description={description}");
        sb.Append($"image_url={imageUrl ?? string.Empty}");
        return sb.ToString();
    }

    public static string BuildDeletePayload(
        string podcastId,
        string publisherPubKey,
        long timestampUnix,
        string nonce,
        string? reason)
    {
        var sb = new StringBuilder();
        sb.AppendLine("action=delete_podcast");
        sb.AppendLine($"podcast_id={podcastId}");
        sb.AppendLine($"publisher_pubkey={publisherPubKey}");
        sb.AppendLine($"timestamp_unix={timestampUnix}");
        sb.AppendLine($"nonce={nonce}");
        sb.Append($"reason={reason ?? string.Empty}");
        return sb.ToString();
    }

    private void CleanupExpiredNonces()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _seenNonces)
        {
            if (kv.Value <= now)
                _seenNonces.TryRemove(kv.Key, out _);
        }
    }
}
