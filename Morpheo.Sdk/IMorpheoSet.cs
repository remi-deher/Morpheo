namespace Morpheo.Sdk;

public record EntityChange<T>(T? Entity, string Action);

public interface IMorpheoSet<T> where T : MorpheoEntity
{
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(string id);
    IQueryable<T> Query();
    IDisposable Observe(Action<EntityChange<T>> callback);
}
