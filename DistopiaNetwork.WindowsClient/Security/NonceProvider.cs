namespace DistopiaNetwork.WindowsClient.Security;

public class NonceProvider
{
    public string Next() => Guid.NewGuid().ToString("N");
}
