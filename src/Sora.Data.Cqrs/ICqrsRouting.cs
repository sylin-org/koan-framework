using Sora.Data.Abstractions;

namespace Sora.Data.Cqrs;

public interface ICqrsRouting
{
    string? GetProfileNameFor(Type entityType);
    IDataRepository<TEntity, TKey> GetReadRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull;
    IDataRepository<TEntity, TKey> GetWriteRepository<TEntity, TKey>() where TEntity : class, IEntity<TKey> where TKey : notnull;
}