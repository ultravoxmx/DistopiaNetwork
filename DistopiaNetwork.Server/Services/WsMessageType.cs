namespace DistopiaNetwork.Server.Services;

/// <summary>
/// Costanti per i tipi di messaggio del protocollo WebSocket
/// tra server e publisher client.
/// 
/// Protocollo completo:
/// 
///   Client → Server:
///     HELLO         { publisher_pub_key }     Prima connessione, autenticazione
///     FILE_DATA     { file_hash, file_data_base64 }  Risposta a REQUEST_FILE
///     FILE_NOT_FOUND { file_hash }            Il file non esiste sul client
///     PONG          {}                        Risposta al PING del server
/// 
///   Server → Client:
///     HELLO_ACK     {}                        Conferma autenticazione
///     REQUEST_FILE  { file_hash }             Richiesta file MP3
///     PING          {}                        Keep-alive
/// </summary>
public static class WsMessageType
{
    // Client → Server
    public const string Hello        = "HELLO";
    public const string FileData     = "FILE_DATA";
    public const string FileNotFound = "FILE_NOT_FOUND";
    public const string Pong         = "PONG";

    // Server → Client
    public const string HelloAck    = "HELLO_ACK";
    public const string RequestFile = "REQUEST_FILE";
    public const string Ping        = "PING";
}
