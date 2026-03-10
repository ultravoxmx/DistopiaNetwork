using DistopiaNetwork.BrowserClient.Configuration;
using DistopiaNetwork.BrowserClient.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.Configure<BrowserSettings>(
            ctx.Configuration.GetSection(BrowserSettings.Section));
        services.AddSingleton<CatalogClient>();
        services.AddSingleton<StreamClient>();
        services.AddHttpClient();
    })
    .Build();

var catalog = host.Services.GetRequiredService<CatalogClient>();
var stream = host.Services.GetRequiredService<StreamClient>();
var settings = host.Services.GetRequiredService<IOptions<BrowserSettings>>().Value;

Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║   DISTOPIA - Browser Client v1.0     ║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.WriteLine($"Connected to: {settings.ServerUrl}");
Console.WriteLine();

while (true)
{
    Console.WriteLine("Commands: [l]ist  [s]tream  [q]uit");
    Console.Write("> ");
    var cmd = Console.ReadLine()?.Trim().ToLower();

    if (cmd == "q" || cmd == "quit") break;

    if (cmd == "l" || cmd == "list")
    {
        Console.WriteLine("\nFetching catalog...");
        var podcasts = await catalog.GetAllAsync();

        if (!podcasts.Any())
        {
            Console.WriteLine("No podcasts in catalog.");
        }
        else
        {
            Console.WriteLine($"\n{"#",-3} {"ID",-38} {"Title",-30} {"Duration"}");
            Console.WriteLine(new string('-', 85));
            int i = 1;
            foreach (var p in podcasts)
            {
                var dur = TimeSpan.FromSeconds(p.DurationSeconds);
                Console.WriteLine($"{i++,-3} {p.PodcastId,-38} {p.Title,-30} {dur:mm\\:ss}");
            }
        }
    }
    else if (cmd == "s" || cmd == "stream")
    {
        Console.Write("Podcast ID: ");
        var id = Console.ReadLine()?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(id)) continue;

        var meta = await catalog.GetByIdAsync(id);
        if (meta is null) { Console.WriteLine("Podcast not found."); continue; }

        Console.WriteLine($"Streaming: {meta.Title}");
        var outputPath = await stream.DownloadAsync(id, $"{meta.PodcastId}.mp3");
        Console.WriteLine(outputPath is not null
            ? $"✓ Saved to: {outputPath}"
            : "✗ Stream failed.");
    }
    else
    {
        Console.WriteLine("Unknown command.");
    }

    Console.WriteLine();
}
