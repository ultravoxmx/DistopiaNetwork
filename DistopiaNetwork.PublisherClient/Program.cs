using DistopiaNetwork.PublisherClient.Configuration;
using DistopiaNetwork.PublisherClient.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<PublisherSettings>(
            ctx.Configuration.GetSection(PublisherSettings.Section));
        services.AddSingleton<KeyStore>();
        services.AddScoped<PublishService>();
        services.AddHttpClient();
    })
    .Build();

var publish = host.Services.GetRequiredService<PublishService>();
var settings = host.Services.GetRequiredService<IOptions<PublisherSettings>>().Value;
var keys = host.Services.GetRequiredService<KeyStore>();

Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║   DISTOPIA - Publisher Client v1.0   ║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.WriteLine($"Server : {settings.ServerUrl}");
Console.WriteLine($"Active : {settings.IsActive}");
Console.WriteLine($"PubKey : {keys.PublicKey[..32]}...");
Console.WriteLine();

while (true)
{
    Console.WriteLine("Commands: [p]ublish  [q]uit");
    Console.Write("> ");
    var cmd = Console.ReadLine()?.Trim().ToLower();

    if (cmd == "q" || cmd == "quit") break;

    if (cmd == "p" || cmd == "publish")
    {
        Console.Write("MP3 file path: ");
        var path = Console.ReadLine()?.Trim() ?? string.Empty;
        if (!File.Exists(path)) { Console.WriteLine("File not found."); continue; }

        Console.Write("Title: ");
        var title = Console.ReadLine() ?? "Untitled";

        Console.Write("Description: ");
        var description = Console.ReadLine() ?? string.Empty;

        Console.Write("Duration (seconds): ");
        int.TryParse(Console.ReadLine(), out var dur);

        Console.Write("Cover image URL (optional): ");
        var imgUrl = Console.ReadLine()?.Trim();

        Console.WriteLine("\nPublishing...");
        var success = await publish.PublishAsync(path, title, description, dur,
            string.IsNullOrEmpty(imgUrl) ? null : imgUrl);

        Console.WriteLine(success ? "✓ Published successfully!" : "✗ Publish failed. Check logs.");
    }
    else
    {
        Console.WriteLine("Unknown command.");
    }

    Console.WriteLine();
}
