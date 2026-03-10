using DistopiaNetwork.Server.Services;
using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace DistopiaNetwork.Server.Controllers;

[ApiController]
[Route("")]
public class PodcastController : ControllerBase
{
    private readonly CatalogService _catalog;
    private readonly CacheService _cache;
    private readonly StreamingService _streaming;
    private readonly ILogger<PodcastController> _logger;

    public PodcastController(
        CatalogService catalog,
        CacheService cache,
        StreamingService streaming,
        ILogger<PodcastController> logger)
    {
        _catalog  = catalog;
        _cache    = cache;
        _streaming = streaming;
        _logger   = logger;
    }

    // ── GET /podcasts ─────────────────────────────────────────────────────────
    /// <summary>Ritorna tutti i podcast nel catalogo.</summary>
    [HttpGet("podcasts")]
    public async Task<IActionResult> GetAll()
    {
        var podcasts = await _catalog.GetAllAsync();
        return Ok(podcasts);
    }

    // ── GET /podcasts/since/{timestamp} ───────────────────────────────────────
    /// <summary>Sincronizzazione incrementale: podcast pubblicati dopo il timestamp.</summary>
    [HttpGet("podcasts/since/{timestamp:long}")]
    public async Task<IActionResult> GetSince(long timestamp)
    {
        var podcasts = await _catalog.GetSinceAsync(timestamp);
        return Ok(new SyncResponse { Podcasts = podcasts.ToList() });
    }

    // ── GET /podcasts/{id} ────────────────────────────────────────────────────
    /// <summary>Ritorna un singolo podcast per ID.</summary>
    [HttpGet("podcasts/{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var podcast = await _catalog.GetAsync(id);
        if (podcast is null)
            return NotFound($"Podcast '{id}' not found.");
        return Ok(podcast);
    }

    // ── POST /podcast/publish ─────────────────────────────────────────────────
    /// <summary>
    /// Step 1 della pubblicazione: riceve i metadati firmati dal publisher client.
    /// Verifica la firma RSA e rileva duplicati prima di accettare.
    /// </summary>
    [HttpPost("podcast/publish")]
    public async Task<IActionResult> Publish([FromBody] PodcastMetadata metadata)
    {
        // Controllo duplicato anticipato — risponde 409 Conflict con titolo esistente
        var existing = await _catalog.FindByFileHashAsync(metadata.FileHash);
        if (existing is not null && existing.PodcastId != metadata.PodcastId)
        {
            return Conflict(new PublishResponse
            {
                Success = false,
                Error = $"Duplicate: already published as '{existing.Title}'"
            });
        }

        var ok = await _catalog.TryAddOrUpdateAsync(metadata);
        if (!ok)
        {
            return BadRequest(new PublishResponse
            {
                Success = false,
                Error = "Invalid signature or duplicate content."
            });
        }

        return Ok(new PublishResponse
        {
            Success   = true,
            UploadUrl = $"/podcast/{metadata.PodcastId}/upload"
        });
    }

    // ── POST /podcast/{id}/upload ─────────────────────────────────────────────
    /// <summary>
    /// Step 2 della pubblicazione: riceve il file MP3 e lo verifica via SHA-256.
    /// </summary>
    [HttpPost("podcast/{id}/upload")]
    [RequestSizeLimit(500_000_000)] // 500 MB
    public async Task<IActionResult> UploadMp3(string id, CancellationToken ct)
    {
        var metadata = await _catalog.GetAsync(id);
        if (metadata is null)
            return NotFound($"Podcast '{id}' not found in catalog. Publish metadata first.");

        // File già in cache? Drain del body per TCP corretto, poi rispondi 200
        if (await _cache.HasAsync(metadata.FileHash))
        {
            await Request.Body.CopyToAsync(System.IO.Stream.Null, ct);
            return Ok("Already exists in cache. Upload skipped.");
        }

        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms, ct);
        var data = ms.ToArray();

        if (!DistopiaNetwork.Shared.Crypto.CryptoHelper.VerifyFileHash(data, metadata.FileHash))
        {
            _logger.LogWarning("Hash mismatch on upload for podcast {Id}", id);
            return BadRequest("File hash mismatch. The uploaded file does not match the declared SHA-256.");
        }

        await _cache.StoreBytesAsync(metadata.FileHash, data, id);
        _logger.LogInformation("MP3 stored for podcast {Id}", id);

        return Ok("Upload successful.");
    }

    // ── GET /podcast/{id}/stream ──────────────────────────────────────────────
    /// <summary>
    /// Streaming audio al browser. Supporta Range requests per seek e resume.
    /// </summary>
    [HttpGet("podcast/{id}/stream")]
    public async Task<IActionResult> Stream(string id, CancellationToken ct)
    {
        var (stream, contentType, _) = await _streaming.ResolveStreamAsync(id, ct);

        if (stream is null)
            return NotFound("Audio file not available. Try again later.");

        // Accept-Ranges: bytes abilita seek nativo nell'audio player HTML5
        Response.Headers.Append("Accept-Ranges", "bytes");

        return File(stream, contentType ?? "audio/mpeg", enableRangeProcessing: true);
    }

    // ── GET /internal/file/{fileHash} ─────────────────────────────────────────
    /// <summary>
    /// Endpoint server-to-server: serve un MP3 per hash SHA-256.
    /// Non destinato ai browser — usato da StreamingService quando la cache è fredda.
    /// </summary>
    [HttpGet("internal/file/{fileHash}")]
    public async Task<IActionResult> GetFileByHash(string fileHash, CancellationToken ct)
    {
        var (stream, contentType, _) = await _streaming.ResolveStreamByHashAsync(fileHash, ct);

        if (stream is null)
            return NotFound($"File '{fileHash}' not available.");

        return File(stream, contentType ?? "audio/mpeg", enableRangeProcessing: true);
    }

    // ── GET /status ───────────────────────────────────────────────────────────
    /// <summary>Info sul nodo: catalogo, cache, server ID.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(
        [FromServices] Microsoft.Extensions.Options.IOptions<Configuration.ServerSettings> settings)
    {
        return Ok(new
        {
            serverId     = settings.Value.ServerId,
            catalogSize  = _catalog.Count(),
            cachedFiles  = await _cache.CountActiveAsync(),
            timestamp    = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });
    }
}
