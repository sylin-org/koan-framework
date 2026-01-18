using System.Collections.Concurrent;
using Koan.Data.Abstractions;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;

namespace Koan.Tests.Data.Core.Support;

/// <summary>
/// Fake vector service for testing that provides in-memory repositories.
/// Allows error injection and state inspection across all entity types.
/// </summary>
public sealed class FakeVectorService : IVectorService
{
    private readonly ConcurrentDictionary<Type, object> _repositories = new();

    public IVectorSearchRepository<TEntity, TKey>? TryGetRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var entityType = typeof(TEntity);

        var repo = _repositories.GetOrAdd(entityType, _ =>
            new FakeVectorRepository<TEntity, TKey>());

        return (IVectorSearchRepository<TEntity, TKey>)repo;
    }

    /// <summary>
    /// Gets the fake repository for a specific entity type (for test assertions).
    /// </summary>
    public FakeVectorRepository<TEntity, TKey> GetFakeRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        var repo = TryGetRepository<TEntity, TKey>();
        if (repo == null)
        {
            throw new InvalidOperationException($"No repository found for {typeof(TEntity).Name}");
        }

        return (FakeVectorRepository<TEntity, TKey>)repo;
    }

    /// <summary>
    /// Clears all vectors from all repositories.
    /// </summary>
    public void ClearAll()
    {
        foreach (var repo in _repositories.Values)
        {
            if (repo is FakeVectorRepository<IEntity<string>, string> stringRepo)
            {
                stringRepo.Clear();
            }
        }
    }
}
