namespace DistopiaNetwork.Server.Data.Repositories;

/// <summary>
/// Interfaccia generica Repository. Definisce le operazioni CRUD di base
/// condivise da tutti i repository concreti.
/// </summary>
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(string id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    void Update(T entity);
    void Remove(T entity);
}
