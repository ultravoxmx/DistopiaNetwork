using System;
using DistopiaNetwork.WindowsClient.Api;
using DistopiaNetwork.WindowsClient.Configuration;
using DistopiaNetwork.WindowsClient.Domain.Services;
using DistopiaNetwork.WindowsClient.Security;
using DistopiaNetwork.WindowsClient.UI.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
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

                services.AddHttpClient();
                services.AddSingleton<PodcastApiClient>();

                services.AddSingleton<CatalogService>();
                services.AddSingleton<PublishService>();
                services.AddSingleton<MetadataEditService>();
                services.AddSingleton<DeleteService>();

                services.AddSingleton<MainExplorerForm>();
            })
            .Build();

        ApplicationConfiguration.Initialize();
        Application.Run(host.Services.GetRequiredService<MainExplorerForm>());
    }
}
