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
            // Retry automatico per disconnessioni transitorie (Azure, cloud)
            sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
        }
    )
);

// ── Repository e Unit of Work (tutti Scoped: vivono per la durata di ogni richiesta) ──
builder.Services.AddScoped<IPodcastRepository, PodcastRepository>();
builder.Services.AddScoped<ICacheEntryRepository, CacheEntryRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ── Services applicativi ──────────────────────────────────────────────────────
// CatalogService e CacheService ora sono Scoped (dipendono da IUnitOfWork Scoped)
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CacheService>();
builder.Services.AddScoped<StreamingService>();

// ── Background Services (Singleton, usano IServiceScopeFactory internamente) ──
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
// Applica tutte le migrazioni pendenti. In produzione valuta di separare
// questo step dalla pipeline di deploy con: dotnet ef database update
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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

// UseDefaultFiles DEVE precedere UseStaticFiles
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});
app.UseStaticFiles();
app.MapControllers();

app.Run();
