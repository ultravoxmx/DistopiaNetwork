using System.Text;

namespace DistopiaNetwork.WindowsClient.Security;

public static class SignedPayloadBuilder
{
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
}
