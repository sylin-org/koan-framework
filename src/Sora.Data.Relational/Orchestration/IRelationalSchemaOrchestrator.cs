using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Relational.Orchestration;

public interface IRelationalDdlExecutor
{
    bool TableExists(string schema, string table);
    bool ColumnExists(string schema, string table, string column);
    void CreateTableIdJson(string schema, string table, string idColumn = "Id", string jsonColumn = "Json");
    void AddComputedColumnFromJson(string schema, string table, string column, string jsonPath, bool persisted);
    void AddPhysicalColumn(string schema, string table, string column, Type clrType, bool nullable);
    void CreateIndex(string schema, string table, string indexName, IReadOnlyList<string> columns, bool unique);
}

public interface IRelationalStoreFeatures
{
    bool SupportsJsonFunctions { get; }
    bool SupportsPersistedComputedColumns { get; }
    bool SupportsIndexesOnComputedColumns { get; }
}

public interface IRelationalSchemaOrchestrator
{
    Task<object> ValidateAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Sora.Data.Abstractions.IEntity<TKey>
        where TKey : notnull;
    Task EnsureCreatedAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Sora.Data.Abstractions.IEntity<TKey>
        where TKey : notnull;
    Task EnsureCreatedJsonAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Sora.Data.Abstractions.IEntity<TKey>
        where TKey : notnull;
    Task EnsureCreatedMaterializedAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Sora.Data.Abstractions.IEntity<TKey>
        where TKey : notnull;
}
