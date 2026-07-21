using Koan.Data.Abstractions;

namespace Koan.Data.Relational.Orchestration;

/// <summary>Applies an explicit provider/source schema policy to an Entity store.</summary>
public interface IRelationalSchemaOrchestrator
{
    Task<IReadOnlyDictionary<string, object?>> ValidateAsync<TEntity, TKey>(
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        string table,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;

    Task EnsureCreatedAsync<TEntity, TKey>(
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        string table,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull;
}
