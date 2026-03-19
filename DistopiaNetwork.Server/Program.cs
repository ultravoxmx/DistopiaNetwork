using DistopiaNetwork.Server.Configuration;
using DistopiaNetwork.Server.Data;
using DistopiaNetwork.Server.Data.Repositories;
using DistopiaNetwork.Server.Data.UnitOfWork;
using DistopiaNetwork.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── Configurazione ────────────────────────────────────────────────────────────
builder.Services.Configure<ServerSettings>(
    builder.Configuration.GetSection(ServerSettings.Section));

// ── Database (SQL Server + EF Core) ──────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql =>
        {
            sql.MigrationsAssembly("DistopiaNetwork.Server");
            sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
        }
    )
);

// ── Repository e Unit of Work ─────────────────────────────────────────────────
builder.Services.AddScoped<IPodcastRepository, PodcastRepository>();
builder.Services.AddScoped<ICacheEntryRepository, CacheEntryRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ── Services applicativi ──────────────────────────────────────────────────────
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CacheService>();
builder.Services.AddScoped<StreamingService>();
builder.Services.AddSingleton<SignedRequestVerifier>();

// ── WebSocket: gestore connessioni publisher (Singleton: vive per tutto il processo) ──
// PublisherConnectionManager è Singleton perché mantiene il dizionario delle connessioni
// attive tra tutte le richieste HTTP. Non può essere Scoped.
builder.Services.AddSingleton<PublisherConnectionManager>();

// PublisherWebSocketHandler è Transient: una nuova istanza per ogni connessione WS
builder.Services.AddTransient<PublisherWebSocketHandler>();

// ── Background Services ───────────────────────────────────────────────────────
builder.Services.AddHostedService<SyncService>();
builder.Services.AddHostedService<CacheCleanupService>();

// ── HTTP + API ────────────────────────────────────────────────────────────────
builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Distopia Network API", Version = "v1" });
});

var app = builder.Build();

// ── Migrazione automatica all'avvio ──────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync();
        logger.LogInformation("Database ready.");
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "Failed to apply migrations. Check connection string.");
        throw;
    }
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// WebSocket deve essere abilitato PRIMA di MapControllers
app.UseWebSockets(new WebSocketOptions
{
    // Ping automatico ogni 30s per rilevare connessioni zombie
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// ── Endpoint WebSocket per publisher client ───────────────────────────────────
// I publisher client si connettono a ws://server/ws/publisher all'avvio
// e mantengono la connessione aperta per rispondere alle richieste di file.
app.Map("/ws/publisher", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("WebSocket connection required.");
        return;
    }

    var ws      = await context.WebSockets.AcceptWebSocketAsync();
    var handler = context.RequestServices.GetRequiredService<PublisherWebSocketHandler>();

    // HandleAsync blocca finché la connessione non viene chiusa
    await handler.HandleAsync(ws, context.RequestAborted);
});

app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});
app.UseStaticFiles();
app.MapControllers();

app.Run();
