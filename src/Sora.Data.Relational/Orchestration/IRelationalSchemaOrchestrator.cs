namespace Sora.Data.Relational.Orchestration;

public interface IRelationalSchemaOrchestrator
{
    Task<object> ValidateAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull;
    Task EnsureCreatedAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull;
    Task EnsureCreatedJsonAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull;
    Task EnsureCreatedMaterializedAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull;
}
