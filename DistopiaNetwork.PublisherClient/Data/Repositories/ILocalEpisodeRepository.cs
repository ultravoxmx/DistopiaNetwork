using DistopiaNetwork.PublisherClient.Entities;

namespace DistopiaNetwork.PublisherClient.Data.Repositories;

/// <summary>
/// Interfaccia Repository per gli episodi locali del publisher.
/// </summary>
public interface ILocalEpisodeRepository
{
    Task<LocalEpisodeEntity?> GetByIdAsync(string podcastId);

    /// <summary>
    /// Tutti gli episodi in ordine cronologico decrescente.
    /// </summary>
    Task<IEnumerable<LocalEpisodeEntity>> GetAllAsync();

    /// <summary>
    /// Cerca un episodio per SHA-256 del file.
    /// Usato per rilevare duplicati locali prima di contattare il server.
    /// </summary>
    Task<LocalEpisodeEntity?> FindByHashAsync(string fileHash);

    /// <summary>
    /// Ritorna gli episodi in stato Draft o Failed.
    /// Usato per riprendere upload interrotti al prossimo avvio.
    /// </summary>
    Task<IEnumerable<LocalEpisodeEntity>> GetPendingAsync();

    /// <summary>
    /// Ritorna gli episodi filtrati per stato specifico.
    /// </summary>
    Task<IEnumerable<LocalEpisodeEntity>> GetAllByStatusAsync(string status);

    Task AddAsync(LocalEpisodeEntity entity);
    void Update(LocalEpisodeEntity entity);
    void Remove(LocalEpisodeEntity entity);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
