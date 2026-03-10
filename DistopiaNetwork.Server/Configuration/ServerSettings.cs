namespace DistopiaNetwork.Server.Configuration;

public class ServerSettings
{
    public const string Section = "ServerSettings";

    public string ServerId { get; set; } = "server-default";
    public int Port { get; set; } = 5000;
    public string CacheDirectory { get; set; } = "./mp3cache";
    public List<string> PeerServers { get; set; } = new();
    public int SyncIntervalSeconds { get; set; } = 60;
    public int CacheCleanupIntervalSeconds { get; set; } = 3600;
}
