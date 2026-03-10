using DistopiaNetwork.Server.Services;
using DistopiaNetwork.Shared.Crypto;
using DistopiaNetwork.Shared.Dto;
using DistopiaNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace DistopiaNetwork.Server.Controllers;

/// <summary>
/// Public API for podcast catalog browsing and streaming.
/// Route prefix [Route("")] keeps paths flat (no /api/ prefix).
/// </summary>
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
        _catalog = catalog;
        _cache = cache;
        _streaming = streaming;
        _logger = logger;
    }

    // ─── Catalog ────────────────────────────────────────────────────────────────

    /// <summary>List all podcasts in the catalog.</summary>
    [HttpGet("podcasts")]
    public IActionResult GetAll()
        => Ok(_catalog.GetAll());

    // IMPORTANT: /podcasts/since/{timestamp} MUST be declared BEFORE /podcasts/{id}
    // otherwise ASP.NET Core matches "since" as the {id} parameter and either
    // crashes or returns wrong data, causing "response ended prematurely" on the client.
    /// <summary>
    /// Sync endpoint: returns all podcasts with publish_timestamp > timestamp.
    /// Used by peer servers and the web UI for incremental refresh.
    /// </summary>
    [HttpGet("podcasts/since/{timestamp:long}")]
    public IActionResult GetSince(long timestamp)
        => Ok(new SyncResponse { Podcasts = _catalog.GetSince(timestamp).ToList() });

    /// <summary>Get a single podcast by ID.</summary>
    [HttpGet("podcasts/{id}")]
    public IActionResult GetById(string id)
    {
        var podcast = _catalog.Get(id);
        return podcast is null ? NotFound() : Ok(podcast);
    }

    // ─── Publisher ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Step 1 of publication: receive and validate signed metadata.
    /// Returns 409 Conflict if the same file_hash is already registered.
    /// Returns 400 Bad Request if the digital signature is invalid.
    /// Returns 200 OK with { success, uploadUrl } on success.
    /// </summary>
    [HttpPost("podcast/publish")]
    public IActionResult Publish([FromBody] PodcastMetadata metadata)
    {
        if (metadata is null)
            return BadRequest(new PublishResponse { Success = false, Error = "Missing metadata body." });

        // Early duplicate check — returns 409 before touching the catalog
        var existing = _catalog.FindByFileHash(metadata.FileHash);
        if (existing is not null && existing.PodcastId != metadata.PodcastId)
        {
            _logger.LogWarning(
                "Duplicate publish attempt: file_hash {Hash} already registered as {ExistingId}.",
                metadata.FileHash, existing.PodcastId);

            return Conflict(new PublishResponse
            {
                Success = false,
                Error = $"Duplicate: this MP3 is already published as episode '{existing.Title}' (ID: {existing.PodcastId})."
            });
        }

        if (!_catalog.TryAddOrUpdate(metadata))
            return BadRequest(new PublishResponse { Success = false, Error = "Invalid signature." });

        _logger.LogInformation("Podcast metadata accepted: {Id} — {Title}", metadata.PodcastId, metadata.Title);

        return Ok(new PublishResponse
        {
            Success  = true,
            UploadUrl = $"/podcast/{metadata.PodcastId}/upload"
        });
    }

    /// <summary>
    /// Step 2 of publication: receive the raw MP3 bytes from the publisher client.
    /// Skips storage (returns 200) if the file hash is already in cache.
    /// Returns 400 if the received bytes do not match the hash in metadata.
    /// </summary>
    [HttpPost("podcast/{id}/upload")]
    [RequestSizeLimit(500_000_000)] // 500 MB max
    public async Task<IActionResult> UploadMp3(string id)
    {
        var metadata = _catalog.Get(id);
        if (metadata is null)
            return NotFound("Podcast metadata not found. Publish metadata first.");

        // Read the entire body FIRST — before any early returns.
        // Attempting to drain with CopyToAsync(Stream.Null) before returning
        // causes "response ended prematurely" in HttpClient on the sender side.
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        var data = ms.ToArray();

        if (data.Length == 0)
            return BadRequest("Empty file body received.");

        // If the file is already cached, discard the bytes we just read and return early.
        if (_cache.Has(metadata.FileHash))
        {
            _logger.LogInformation(
                "Upload skipped for podcast {Id}: file_hash {Hash} already in cache.",
                id, metadata.FileHash);
            return Ok("Already exists. Upload skipped.");
        }

        if (!CryptoHelper.VerifyFileHash(data, metadata.FileHash))
        {
            _logger.LogError(
                "Hash mismatch for podcast {Id}. Expected {Expected}, got {Got}.",
                id, metadata.FileHash, CryptoHelper.ComputeFileHash(data));
            return BadRequest("File hash mismatch. The uploaded bytes do not match the hash in metadata.");
        }

        await _cache.StoreBytesAsync(metadata.FileHash, data);
        _logger.LogInformation("MP3 stored for podcast {Id} ({Size:N0} bytes).", id, data.Length);
        return Ok("Upload successful.");
    }

    // ─── Streaming ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Browser streaming endpoint.
    /// Cascade: local cache → publisher server → 404.
    /// Supports HTTP Range requests for seeking in audio players.
    /// </summary>
    [HttpGet("podcast/{id}/stream")]
    public async Task<IActionResult> Stream(string id, CancellationToken ct)
    {
        var (stream, contentType, _) = await _streaming.ResolveStreamAsync(id, ct);

        if (stream is null)
            return NotFound("Audio file not available.");

        Response.Headers.Append("Accept-Ranges", "bytes");
        return File(stream, contentType ?? "audio/mpeg", enableRangeProcessing: true);
    }

    // ─── Internal (server-to-server) ────────────────────────────────────────────

    /// <summary>
    /// Called by peer servers to fetch a cached MP3 by its SHA-256 hash.
    /// If not locally cached, triggers the full resolution cascade.
    /// </summary>
    [HttpGet("internal/file/{fileHash}")]
    public async Task<IActionResult> GetFile(string fileHash, CancellationToken ct)
    {
        if (_cache.Has(fileHash))
        {
            _logger.LogInformation("Serving cached {Hash} to peer server.", fileHash);
            var fs = _cache.OpenRead(fileHash);
            return File(fs!, "audio/mpeg");
        }

        var podcast = _catalog.GetAll().FirstOrDefault(p => p.FileHash == fileHash);
        if (podcast is null)
            return NotFound("File not found in catalog.");

        var (stream, contentType, _) = await _streaming.ResolveStreamAsync(podcast.PodcastId, ct);
        if (stream is null)
            return NotFound("Could not retrieve file from publisher.");

        return File(stream, contentType ?? "audio/mpeg");
    }

    // ─── Status ──────────────────────────────────────────────────────────────────

    [HttpGet("status")]
    public IActionResult Status(
        [FromServices] Microsoft.Extensions.Options.IOptions<Configuration.ServerSettings> opts)
        => Ok(new
        {
            ServerId    = opts.Value.ServerId,
            CatalogSize = _catalog.Count,
            CachedFiles = _cache.Entries.Count,
            Timestamp   = DateTimeOffset.UtcNow
        });
}
