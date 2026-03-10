namespace DistopiaNetwork.PublisherClient.Configuration;

public class PublisherSettings
{
    public const string Section = "PublisherSettings";

    public string ServerUrl { get; set; } = "https://localhost:55689";
    public string ServerId { get; set; } = "server-a";
    public string PrivateKeyPath { get; set; } = "publisher_private.key";
    public string PublicKeyPath { get; set; } = "publisher_public.key";
    public bool IsActive { get; set; } = true;
    public bool AutoResetCorruptLocalDb { get; set; } = true;
}
