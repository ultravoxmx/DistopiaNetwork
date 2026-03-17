using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DistopiaNetwork.PublisherClient.Configuration;
using DistopiaNetwork.PublisherClient.Data.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DistopiaNetwork.PublisherClient.Services;

/// <summary>
/// Background service che mantiene una connessione WebSocket persistente
/// verso il server di riferimento del publisher.
///
/// Responsabilità:
///   1. Connettersi al server all'avvio (ws://server/ws/publisher)
///   2. Autenticarsi con HELLO + pubkey
///   3. Rispondere alle richieste REQUEST_FILE con i byte del file locale
///   4. Riconnettersi automaticamente in caso di disconnessione
///
/// Questo risolve il problema NAT: il client è sempre lui a iniziare
/// la connessione verso il server, quindi funziona anche da reti private.
/// </summary>
public class ServerConnectionService : BackgroundService
{
    private readonly KeyStore _keyStore;
    private readonly ILocalEpisodeRepository _repo;
    private readonly PublisherSettings _settings;
    private readonly ILogger<ServerConnectionService> _logger;

    // Intervallo di attesa tra tentativi di riconnessione (con backoff)
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2)
    };

    public ServerConnectionService(
        KeyStore keyStore,
        ILocalEpisodeRepository repo,
        IOptions<PublisherSettings> settings,
        ILogger<ServerConnectionService> logger)
    {
        _keyStore = keyStore;
        _repo     = repo;
        _settings = settings.Value;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Se il client non è attivo (client di backup), non si connette
        if (!_settings.IsActive)
        {
            _logger.LogInformation("Publisher client is inactive (backup mode). WebSocket disabled.");
            return;
        }

        _logger.LogInformation("Starting WebSocket connection to server {Url}", _settings.ServerUrl);

        int retryIndex = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndHandleAsync(stoppingToken);

                // Se ConnectAndHandleAsync ritorna senza eccezione (disconnessione pulita),
                // aspetta un po' prima di riconnettersi
                retryIndex = 0;
                _logger.LogInformation("WebSocket disconnected. Reconnecting in 5s...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Server in shutdown — uscita pulita
                break;
            }
            catch (Exception ex)
            {
                var delay = RetryDelays[Math.Min(retryIndex, RetryDelays.Length - 1)];
                retryIndex++;

                _logger.LogWarning(ex, "WebSocket error. Reconnecting in {Delay}s...", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
            }
        }

        _logger.LogInformation("ServerConnectionService stopped.");
    }

    /// <summary>
    /// Stabilisce la connessione WebSocket e gestisce i messaggi
    /// finché la connessione rimane aperta.
    /// </summary>
    private async Task ConnectAndHandleAsync(CancellationToken ct)
    {
        // Costruisce l'URL WebSocket dal ServerUrl HTTP/HTTPS
        // http://server:5000 → ws://server:5000/ws/publisher
        // https://server:5000 → wss://server:5000/ws/publisher
        var wsUrl = BuildWsUrl(_settings.ServerUrl);

        using var ws = new ClientWebSocket();

        _logger.LogInformation("Connecting to {Url}...", wsUrl);
        await ws.ConnectAsync(new Uri(wsUrl), ct);
        _logger.LogInformation("WebSocket connected to {Url}", wsUrl);

        // ── Autenticazione: manda HELLO con la pubkey ─────────────────────────
        await SendAsync(ws, new WsClientMessage
        {
            Type             = WsClientMessageType.Hello,
            PublisherPubKey  = _keyStore.PublicKey
        }, ct);

        // Aspetta HELLO_ACK dal server
        var ack = await ReceiveAsync(ws, ct);
        if (ack?.Type != WsClientMessageType.HelloAck)
        {
            _logger.LogWarning("Expected HELLO_ACK, got: {Type}", ack?.Type);
            await ws.CloseAsync(WebSocketCloseStatus.ProtocolError, "Expected HELLO_ACK", ct);
            return;
        }

        _logger.LogInformation("Authenticated with server. Waiting for file requests...");

        // ── Loop di ricezione messaggi ────────────────────────────────────────
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var message = await ReceiveAsync(ws, ct);
            if (message is null) break;

            switch (message.Type)
            {
                case WsClientMessageType.RequestFile:
                    await HandleFileRequestAsync(ws, message.FileHash!, ct);
                    break;

                case WsClientMessageType.Ping:
                    await SendAsync(ws, new WsClientMessage { Type = WsClientMessageType.Pong }, ct);
                    break;

                default:
                    _logger.LogDebug("Unknown message type from server: {Type}", message.Type);
                    break;
            }
        }
    }

    /// <summary>
    /// Risponde a una richiesta REQUEST_FILE dal server.
    /// Legge il file dal disco locale e lo invia come FILE_DATA.
    /// Se il file non esiste, risponde con FILE_NOT_FOUND.
    /// </summary>
    private async Task HandleFileRequestAsync(WebSocket ws, string fileHash, CancellationToken ct)
    {
        _logger.LogInformation("Server requested file: {Hash}", fileHash);

        // Cerca il file nel DB locale per ottenere il percorso
        var episode = await _repo.FindByHashAsync(fileHash);

        if (episode is null || !File.Exists(episode.LocalFilePath))
        {
            _logger.LogWarning("File not found locally for hash {Hash}", fileHash);
            await SendAsync(ws, new WsClientMessage
            {
                Type     = WsClientMessageType.FileNotFound,
                FileHash = fileHash
            }, ct);
            return;
        }

        _logger.LogInformation("Sending file {Hash} ({Path}) to server...", fileHash, episode.LocalFilePath);

        try
        {
            var data = await File.ReadAllBytesAsync(episode.LocalFilePath, ct);

            await SendAsync(ws, new WsClientMessage
            {
                Type           = WsClientMessageType.FileData,
                FileHash       = fileHash,
                FileDataBase64 = Convert.ToBase64String(data)
            }, ct);

            _logger.LogInformation("File {Hash} sent ({Bytes} bytes).", fileHash, data.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file {Hash}", fileHash);
            await SendAsync(ws, new WsClientMessage
            {
                Type     = WsClientMessageType.FileNotFound,
                FileHash = fileHash
            }, ct);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task SendAsync(WebSocket ws, WsClientMessage message, CancellationToken ct)
    {
        var json  = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private static async Task<WsClientMessage?> ReceiveAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new List<byte>(4096);
        var chunk  = new byte[4096];

        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(chunk, ct);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            buffer.AddRange(chunk[..result.Count]);

        } while (!result.EndOfMessage);

        var json = Encoding.UTF8.GetString(buffer.ToArray());
        return JsonSerializer.Deserialize<WsClientMessage>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static string BuildWsUrl(string httpUrl)
    {
        // https://server:5000 → wss://server:5000/ws/publisher
        // http://server:5000  → ws://server:5000/ws/publisher
        var uri    = new Uri(httpUrl);
        var scheme = uri.Scheme == "https" ? "wss" : "ws";
        return $"{scheme}://{uri.Authority}/ws/publisher";
    }
}

/// <summary>Messaggi inviati dal client al server.</summary>
public class WsClientMessage
{
    public string Type { get; set; } = string.Empty;
    public string? PublisherPubKey { get; set; }
    public string? FileHash { get; set; }
    public string? FileDataBase64 { get; set; }
}

/// <summary>Costanti per i tipi di messaggio lato client.</summary>
public static class WsClientMessageType
{
    public const string Hello       = "HELLO";
    public const string HelloAck    = "HELLO_ACK";
    public const string RequestFile = "REQUEST_FILE";
    public const string FileData    = "FILE_DATA";
    public const string FileNotFound = "FILE_NOT_FOUND";
    public const string Ping        = "PING";
    public const string Pong        = "PONG";
}
