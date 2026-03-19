namespace DistopiaNetwork.WindowsClient.Configuration;

public class WindowsClientSettings
{
    public const string Section = "WindowsClientSettings";

    public string ServerUrl { get; set; } = "https://localhost:55689";
    public string ServerId { get; set; } = "server-a";
    public string PrivateKeyPath { get; set; } = "publisher_private.key";
    public string PublicKeyPath { get; set; } = "publisher_public.key";
    public int RefreshSeconds { get; set; } = 30;
}
