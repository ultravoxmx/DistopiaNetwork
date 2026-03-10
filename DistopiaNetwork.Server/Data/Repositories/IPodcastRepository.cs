using DistopiaNetwork.Server.Entities;

namespace DistopiaNetwork.Server.Data.Repositories;

/// <summary>
/// Repository specializzato per i metadati podcast.
/// Aggiunge query specifiche al dominio oltre alle operazioni CRUD di base.
/// </summary>
public interface IPodcastRepository : IRepository<PodcastEntity>
{
    /// <summary>
    /// Ritorna tutti gli episodi con PublishTimestamp maggiore del valore dato.
    /// Usato da SyncService per la sincronizzazione incrementale inter-server.
    /// </summary>
    Task<IEnumerable<PodcastEntity>> GetSinceAsync(long unixTimestamp);

    /// <summary>
    /// Cerca un episodio con lo stesso FileHash e PublisherPubKey.
    /// Usato per rilevare upload duplicati prima di accettare nuovi metadati.
    /// </summary>
    Task<PodcastEntity?> FindByFileHashAndPublisherAsync(string fileHash, string publisherPubKey);

    /// <summary>
    /// Verifica l'esistenza di un file hash nel catalogo (qualsiasi publisher).
    /// Controllo rapido pre-upload.
    /// </summary>
    Task<bool> ExistsByFileHashAsync(string fileHash);

    /// <summary>
    /// Ritorna tutti gli episodi pubblicati da un determinato server.
    /// Usato per ottimizzare il routing delle richieste di streaming.
    /// </summary>
    Task<IEnumerable<PodcastEntity>> GetByPublisherServerAsync(string serverId);
}
