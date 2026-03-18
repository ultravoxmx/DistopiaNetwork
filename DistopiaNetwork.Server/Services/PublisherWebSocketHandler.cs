using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Gestisce il ciclo di vita di una singola connessione WebSocket
/// con un publisher client.
/// 
/// Responsabilità:
///   1. Autenticare il client (verifica che la pubkey nel messaggio HELLO
///      corrisponda a un publisher nel catalogo)
///   2. Registrare la connessione nel PublisherConnectionManager
///   3. Leggere i messaggi in arrivo (FILE_DATA, PONG, ecc.)
///   4. Gestire la disconnessione pulita
/// 
/// Questo handler viene istanziato una volta per connessione WebSocket,
/// all'interno di PublisherWebSocketMiddleware.
/// </summary>
public class PublisherWebSocketHandler(
    PublisherConnectionManager manager,
    ILogger<PublisherWebSocketHandler> logger)
{
    private readonly PublisherConnectionManager _manager = manager;
    private readonly ILogger<PublisherWebSocketHandler> _logger = logger;

    /// <summary>
    /// Gestisce l'intera sessione WebSocket di un publisher client.
    /// Questo metodo blocca finché la connessione non viene chiusa.
    /// </summary>
    public async Task HandleAsync(WebSocket ws, CancellationToken ct)
    {
        string? publisherPubKey = null;

        try
        {
            // ── Fase 1: attendi il messaggio HELLO ────────────────────────────
            // Il primo messaggio deve essere HELLO con la pubkey del publisher
            var hello = await ReceiveMessageAsync(ws, ct);
            if (hello is null || hello.Type != WsMessageType.Hello || string.IsNullOrEmpty(hello.PublisherPubKey))
            {
                _logger.LogWarning("WebSocket connection rejected: missing or invalid HELLO message.");
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation,
                    "Expected HELLO message with publisher_pubkey", ct);
                return;
            }

            publisherPubKey = hello.PublisherPubKey;
            _manager.Register(publisherPubKey, ws);

            // Conferma la connessione
            await SendMessageAsync(ws, new WsMessage { Type = WsMessageType.HelloAck }, ct);
            _logger.LogInformation("Publisher WebSocket session started for {Key}",
                publisherPubKey[..Math.Min(20, publisherPubKey.Length)]);

            // ── Fase 2: loop di ricezione messaggi ────────────────────────────
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var message = await ReceiveMessageAsync(ws, ct);
                if (message is null) break;

                switch (message.Type)
                {
                    case WsMessageType.FileData:
                    case WsMessageType.FileNotFound:
                        _manager.HandleClientMessage(message);
                        break;

                    case WsMessageType.Ping:
                        await SendMessageAsync(ws, new WsMessage { Type = WsMessageType.Pong }, ct);
                        break;

                    default:
                        _logger.LogDebug("Unknown message type: {Type}", message.Type);
                        break;
                }
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogInformation("Publisher WebSocket closed abruptly.");
        }
        catch (OperationCanceledException)
        {
            // Server in shutdown — normale
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in publisher WebSocket handler.");
        }
        finally
        {
            if (publisherPubKey is not null)
                _manager.Unregister(publisherPubKey);

            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing", ct); }
                catch { /* ignora errori in chiusura */ }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<WsServerMessage?> ReceiveMessageAsync(WebSocket ws, CancellationToken ct)
    {
        // Buffer iniziale 4KB, espandibile per file grandi
        var buffer = new List<byte>(4096);
        var chunk = new byte[4096];

        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(chunk, ct);

            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            buffer.AddRange(chunk[..result.Count]);

        } while (!result.EndOfMessage);

        var json = Encoding.UTF8.GetString([.. buffer]);
        return JsonSerializer.Deserialize<WsServerMessage>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static async Task SendMessageAsync(WebSocket ws, WsMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }
}

/// <summary>
/// Messaggio ricevuto dal client — include PublisherPubKey usata nell'HELLO.
/// </summary>
public class WsServerMessage : WsMessage
{
    public string? PublisherPubKey { get; set; }
}

// Aggiungi le costanti mancanti a WsMessageType
public static partial class WsMessageTypeExtensions
{
    // Questi sono usati solo lato server nel handler
    public const string Hello    = "HELLO";
    public const string HelloAck = "HELLO_ACK";
}
