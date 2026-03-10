using DistopiaNetwork.Server.Configuration;
using DistopiaNetwork.Server.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ────────────────────────────────────────────────────────────
builder.Services.Configure<ServerSettings>(
    builder.Configuration.GetSection(ServerSettings.Section));

// ─── Kestrel: aumento limite body a 500 MB ───────────────────────────────────
// CRITICAL: senza questo, qualsiasi body > 30 MB viene troncato da Kestrel
// PRIMA che il controller lo legga, causando "response ended prematurely"
// sul client. Il limite per-endpoint [RequestSizeLimit] nel controller
// agisce DOPO questo limite globale, quindi entrambi devono essere impostati.
builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = 500_000_000; // 500 MB
});
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 500_000_000;
});

// ─── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddSingleton<CatalogService>();
builder.Services.AddSingleton<CacheService>();
builder.Services.AddScoped<StreamingService>();

builder.Services.AddHostedService<SyncService>();
builder.Services.AddHostedService<CacheCleanupService>();

// Named "peer" client with extended timeout for large inter-server file transfers
builder.Services.AddHttpClient("peer", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddHttpClient(); // default client

// ─── ASP.NET Core ─────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Distopia Network Server API", Version = "v1" });
});

var app = builder.Build();

// ─── Global exception handler ────────────────────────────────────────────────
// Catches any unhandled exception and returns a clean JSON 500
// instead of dropping the TCP connection (which causes "response ended prematurely")
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        context.Response.StatusCode  = 500;
        context.Response.ContentType = "application/json";

        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var ex      = feature?.Error;

        var logger = context.RequestServices
            .GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Unhandled exception on {Method} {Path}",
            context.Request.Method, context.Request.Path);

        await context.Response.WriteAsJsonAsync(new
        {
            error   = "Internal server error.",
            detail  = ex?.Message,
            path    = context.Request.Path.Value
        });
    });
});

// ─── Swagger ──────────────────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI();

// ─── Static files ─────────────────────────────────────────────────────────────
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});
app.UseStaticFiles();

// ─── Controllers ──────────────────────────────────────────────────────────────
app.MapControllers();

app.Run();
