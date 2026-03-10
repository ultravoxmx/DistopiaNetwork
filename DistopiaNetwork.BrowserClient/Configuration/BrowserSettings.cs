namespace DistopiaNetwork.BrowserClient.Configuration;

public class BrowserSettings
{
    public const string Section = "BrowserSettings";
    public string ServerUrl { get; set; } = "https://localhost:55689";
    public string DownloadDirectory { get; set; } = "./downloads";
}
