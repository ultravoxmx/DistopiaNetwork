using DistopiaNetwork.WindowsClient.Api;
using DistopiaNetwork.WindowsClient.Configuration;
using DistopiaNetwork.WindowsClient.Data;
using DistopiaNetwork.WindowsClient.Domain.Services;
using DistopiaNetwork.WindowsClient.Security;
using DistopiaNetwork.WindowsClient.UI.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.WindowsClient;

internal static class Program
{
    private static void Main(string[] args)
    {
        var uiThread = new Thread(() => RunOnStaThread(args));
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();
    }

    private static void RunOnStaThread(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(cfg =>
            {
                cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<WindowsClientSettings>(ctx.Configuration.GetSection(WindowsClientSettings.Section));
                services.AddSingleton<KeyStore>();
                services.AddSingleton<RequestSigner>();
                services.AddSingleton<NonceProvider>();

                services.AddDbContext<WindowsClientDbContext>((sp, options) =>
                {
                    var settings = sp.GetRequiredService<IOptions<WindowsClientSettings>>().Value;

                    var dbPath = settings.LocalDbPath;
                    if (string.IsNullOrWhiteSpace(dbPath))
                    {
                        var dbDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "DistopiaNetwork");
                        Directory.CreateDirectory(dbDir);
                        dbPath = Path.Combine(dbDir, "publisher.db");
                    }

                    options.UseSqlite($"Data Source={dbPath}");
                });

                services.AddScoped<ILocalEpisodeRepository, LocalEpisodeRepository>();

                services.AddHttpClient("windows-client-http")
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = (request, cert, chain, errors) =>
                        {
                            var host = request?.RequestUri?.Host;
                            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
                                return true;

                            return errors == System.Net.Security.SslPolicyErrors.None;
                        }
                    });
                services.AddScoped<PodcastApiClient>();

                services.AddScoped<CatalogService>();
                services.AddScoped<PublishService>();
                services.AddScoped<MetadataEditService>();
                services.AddScoped<DeleteService>();

                services.AddSingleton<MainExplorerForm>();
            })
            .Build();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WindowsClientDbContext>();
            db.Database.EnsureCreated();
        }

        ApplicationConfiguration.Initialize();
        Application.Run(host.Services.GetRequiredService<MainExplorerForm>());
    }
}
