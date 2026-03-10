using DistopiaNetwork.PublisherClient.Configuration;
using DistopiaNetwork.PublisherClient.Data;
using DistopiaNetwork.PublisherClient.Data.Repositories;
using DistopiaNetwork.PublisherClient.Entities;
using DistopiaNetwork.PublisherClient.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ── Setup Host e Dependency Injection ────────────────────────────────────────
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        // Configurazione da appsettings.json
        services.Configure<PublisherSettings>(
            ctx.Configuration.GetSection(PublisherSettings.Section));

        // ── SQLite ────────────────────────────────────────────────────────────
        // Il file .db viene creato in %LOCALAPPDATA%/DistopiaNetwork/publisher.db
        // Su Linux/macOS: ~/.local/share/DistopiaNetwork/publisher.db
        var dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DistopiaNetwork");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "publisher.db");

        services.AddDbContext<PublisherDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}")
        );

        // Repository (Scoped: una istanza per scope DI)
        services.AddScoped<ILocalEpisodeRepository, LocalEpisodeRepository>();

        // Services applicativi
        services.AddSingleton<KeyStore>();
        services.AddScoped<PublishService>();

        services.AddHttpClient();
    })
    .Build();

// ── Migrazione automatica SQLite all'avvio ────────────────────────────────────
// Crea il file .db e le tabelle se non esistono ancora
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PublisherDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await db.Database.MigrateAsync();
    logger.LogInformation("SQLite database ready at: {Path}", db.Database.GetDbConnection().DataSource);
}

// ── Riprendi upload interrotti ────────────────────────────────────────────────
// All'avvio, controlla se ci sono episodi in Draft o Failed dal run precedente
using (var scope = host.Services.CreateScope())
{
    var publishService = scope.ServiceProvider.GetRequiredService<PublishService>();
    var pending = (await publishService.GetPendingEpisodesAsync()).ToList();
    if (pending.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n⚠ {pending.Count} episodio/i con upload incompleto:");
        foreach (var ep in pending)
            Console.WriteLine($"  [{ep.UploadStatus}] {ep.Title} — {ep.LastErrorMessage ?? "nessun errore"}");
        Console.WriteLine("  Digita 'r' per riprovare tutti, o premi Invio per ignorare.");
        Console.ResetColor();
    }
}

// ── REPL interattivo ──────────────────────────────────────────────────────────
Console.WriteLine("\n=== Distopia Network — Publisher Client ===");
Console.WriteLine("Comandi: [p] Pubblica  [l] Lista episodi  [r] Riprova falliti  [q] Esci\n");

while (true)
{
    Console.Write("> ");
    var cmd = Console.ReadLine()?.Trim().ToLower();

    if (cmd == "q") break;

    if (cmd == "p")
    {
        Console.Write("Percorso file MP3: ");
        var filePath = Console.ReadLine()?.Trim() ?? string.Empty;

        Console.Write("Titolo: ");
        var title = Console.ReadLine()?.Trim() ?? string.Empty;

        Console.Write("Descrizione: ");
        var description = Console.ReadLine()?.Trim() ?? string.Empty;

        Console.Write("Durata in secondi: ");
        int.TryParse(Console.ReadLine()?.Trim(), out var duration);

        Console.Write("URL immagine copertina (opzionale, Invio per saltare): ");
        var imageUrl = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(imageUrl)) imageUrl = null;

        using var scope = host.Services.CreateScope();
        var publishService = scope.ServiceProvider.GetRequiredService<PublishService>();

        Console.WriteLine("Pubblicazione in corso...");
        var success = await publishService.PublishAsync(filePath, title, description, duration, imageUrl);

        Console.ForegroundColor = success ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine(success ? "✓ Pubblicato con successo!" : "✗ Pubblicazione fallita. Controlla i log.");
        Console.ResetColor();
    }
    else if (cmd == "l")
    {
        using var scope = host.Services.CreateScope();
        var publishService = scope.ServiceProvider.GetRequiredService<PublishService>();
        var all = await publishService.GetPublishedEpisodesAsync();

        Console.WriteLine("\n── Episodi pubblicati ─────────────────────────────");
        foreach (var ep in all)
            Console.WriteLine($"  [{ep.PublishTimestamp}] {ep.Title} ({ep.DurationSeconds / 60}min) — {ep.UploadStatus}");
        Console.WriteLine();
    }
    else if (cmd == "r")
    {
        using var scope = host.Services.CreateScope();
        var publishService = scope.ServiceProvider.GetRequiredService<PublishService>();
        var pending = (await publishService.GetPendingEpisodesAsync()).ToList();

        if (pending.Count == 0)
        {
            Console.WriteLine("Nessun episodio con upload incompleto.");
            continue;
        }

        foreach (var ep in pending)
        {
            Console.WriteLine($"Riprovando: {ep.Title}...");
            await publishService.RetryUploadAsync(ep);
        }
    }
    else
    {
        Console.WriteLine("Comando non riconosciuto. Usa: p | l | r | q");
    }
}
