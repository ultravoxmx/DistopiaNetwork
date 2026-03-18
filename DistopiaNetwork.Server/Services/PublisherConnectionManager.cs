using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Gestisce le connessioni WebSocket attive dei publisher client.
/// 
/// Ogni publisher client si connette all'avvio e mantiene la connessione aperta.
/// Quando il server ha bisogno di un file MP3 che non è in cache, lo richiede
/// attraverso questa connessione — senza dover raggiungere il client direttamente
/// (soluzione al problema NAT/rete privata).
/// 
/// Il registro usa la PublisherPubKey come chiave perché:
///   - è l'identità crittografica del publisher (già nei metadati)
///   - è stabile anche se il client si riconnette da IP diverso
///   - permette di trovare la connessione giusta dato un podcast qualsiasi
/// </summary>
public class PublisherConnectionManager
{
    // pubKey → WebSocket attivo
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new();

    // Pending requests: fileHash → TaskCompletionSource che si risolve quando arriva il file
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pending = new();

    private readonly ILogger<PublisherConnectionManager> _logger;

    public PublisherConnectionManager(ILogger<PublisherConnectionManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registra una nuova connessione WebSocket per un publisher.
    /// Se esiste già una connessione per quella pubkey, la sostituisce
    /// (gestisce il caso di riconnessione o client di backup che subentra).
    /// </summary>
    public void Register(string publisherPubKey, WebSocket ws)
    {
        _connections[publisherPubKey] = ws;
        _logger.LogInformation("Publisher connected: {Key}", TruncateKey(publisherPubKey));
    }

    /// <summary>
    /// Rimuove la connessione WebSocket di un publisher (disconnect/errore).
    /// </summary>
    public void Unregister(string publisherPubKey)
    {
        _connections.TryRemove(publisherPubKey, out _);
        _logger.LogInformation("Publisher disconnected: {Key}", TruncateKey(publisherPubKey));
    }

    /// <summary>
    /// Verifica se un publisher è attualmente connesso.
    /// </summary>
    public bool IsConnected(string publisherPubKey)
        => _connections.TryGetValue(publisherPubKey, out var ws)
           && ws.State == WebSocketState.Open;

    /// <summary>
    /// Richiede un file MP3 al publisher client tramite WebSocket.
    /// 
    /// Manda un messaggio REQUEST_FILE sul WebSocket e aspetta che il client
    /// risponda con i byte del file. Timeout configurabile (default 5 minuti
    /// per file grandi su connessioni lente).
    /// 
    /// Ritorna null se:
    ///   - il publisher non è connesso
    ///   - il client non risponde entro il timeout
    ///   - si verifica un errore di rete
    /// </summary>
    public async Task<byte[]?> RequestFileAsync(
        string publisherPubKey,
        string fileHash,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        if (!_connections.TryGetValue(publisherPubKey, out var ws)
            || ws.State != WebSocketState.Open)
        {
            _logger.LogWarning("Publisher {Key} not connected. Cannot request file {Hash}.",
                TruncateKey(publisherPubKey), fileHash);
            return null;
        }

        // Crea un TCS che verrà completato da HandleClientMessageAsync
        // quando il client invia il file in risposta
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pending.TryAdd(fileHash, tcs))
        {
            // Richiesta già in corso per questo hash — aspetta quella esistente
            _logger.LogDebug("Request already pending for {Hash}, waiting...", fileHash);
            tcs = _pending[fileHash];
        }

        try
        {
            // Manda la richiesta al client
            var request = JsonSerializer.Serialize(new WsMessage
            {
                Type = WsMessageType.RequestFile,
                FileHash = fileHash
            });

            var bytes = Encoding.UTF8.GetBytes(request);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);

            _logger.LogInformation("Sent REQUEST_FILE({Hash}) to publisher {Key}",
                fileHash, TruncateKey(publisherPubKey));

            // Aspetta la risposta con timeout
            var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(5);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(effectiveTimeout);

            try
            {
                return await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Timeout waiting for file {Hash} from publisher {Key}",
                    fileHash, TruncateKey(publisherPubKey));
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting file {Hash} from publisher", fileHash);
            return null;
        }
        finally
        {
            _pending.TryRemove(fileHash, out _);
        }
    }

    /// <summary>
    /// Chiamato da PublisherWebSocketHandler quando arriva un messaggio dal client.
    /// Se è una risposta FILE_DATA, completa il TCS corrispondente.
    /// </summary>
    public void HandleClientMessage(WsMessage message)
    {
        if (message.Type == WsMessageType.FileData
            && message.FileHash is not null
            && message.FileData is not null)
        {
            if (_pending.TryGetValue(message.FileHash, out var tcs))
            {
                _logger.LogInformation("Received FILE_DATA for {Hash} ({Bytes} bytes)",
                    message.FileHash, message.FileData.Length);
                tcs.TrySetResult(message.FileData);
            }
            else
            {
                _logger.LogWarning("Received unrequested FILE_DATA for {Hash}", message.FileHash);
            }
        }
        else if (message.Type == WsMessageType.FileNotFound && message.FileHash is not null)
        {
            if (_pending.TryGetValue(message.FileHash, out var tcs))
            {
                _logger.LogWarning("Publisher reported FILE_NOT_FOUND for {Hash}", message.FileHash);
                tcs.TrySetResult([]);
            }
        }
    }

    private static string TruncateKey(string key)
        => key.Length > 20 ? key[..20] + "..." : key;
}

/// <summary>
/// Protocollo messaggi WebSocket tra server e publisher client.
/// </summary>
public class WsMessage
{
    public string Type { get; set; } = string.Empty;
    public string? FileHash { get; set; }

    // Usato solo per FILE_DATA: i byte del file MP3 codificati in Base64
    // (JSON non supporta byte[] nativamente senza serializzazione esplicita)
    public string? FileDataBase64 { get; set; }

    // Proprietà calcolata per comodità — non serializzata
    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? FileData
        => FileDataBase64 is not null ? Convert.FromBase64String(FileDataBase64) : null;
}

/// <summary>Costanti per i tipi di messaggio del protocollo WebSocket.</summary>
//public static class WsMessageType
//{
//    public const string RequestFile = "REQUEST_FILE";
//    public const string FileData    = "FILE_DATA";
//    public const string FileNotFound = "FILE_NOT_FOUND";
//    public const string Ping        = "PING";
//    public const string Pong        = "PONG";
//}
public static class WsMessageType
{
    // Client → Server
    public const string Hello = "HELLO";
    public const string FileData = "FILE_DATA";
    public const string FileNotFound = "FILE_NOT_FOUND";
    public const string Pong = "PONG";

    // Server → Client
    public const string HelloAck = "HELLO_ACK";
    public const string RequestFile = "REQUEST_FILE";
    public const string Ping = "PING";
}