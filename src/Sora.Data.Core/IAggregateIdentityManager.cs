using Sora.Data.Abstractions;

namespace Sora.Data.Core;

public interface IAggregateIdentityManager
{
    ValueTask EnsureIdAsync<TEntity, TKey>(TEntity entity, CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}