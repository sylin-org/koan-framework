using Koan.Data.Core;
using Koan.Data.Core.Optimization;
using Microsoft.Extensions.Logging;

namespace Koan.Data.Relational.Orchestration;

internal sealed class RelationalSchemaOrchestrator : IRelationalSchemaOrchestrator
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<RelationalSchemaOrchestrator>? _logger;

    public RelationalSchemaOrchestrator(
        IServiceProvider sp,
        ILogger<RelationalSchemaOrchestrator>? logger = null)
    {
        _sp = sp;
        _logger = logger;
    }

    public Task<IReadOnlyDictionary<string, object?>> ValidateAsync<TEntity, TKey>(
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        string table,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        ct.ThrowIfCancellationRequested();
        var entity = typeof(TEntity);
        var schema = ResolveSchema(policy);
        var required = GetRequiredColumns(entity, policy);
        var missing = required.Where(c => !ddl.ColumnExists(schema, table, c)).ToArray();
        var tableExists = ddl.TableExists(schema, table);
        var state = ComputeState(tableExists, missing.Length == 0, policy);
        IReadOnlyDictionary<string, object?> report = new Dictionary<string, object?>
        {
            ["Provider"] = features.ProviderName,
            ["Schema"] = schema,
            ["Table"] = table,
            ["TableExists"] = tableExists,
            ["ProjectedColumns"] = required,
            ["MissingColumns"] = missing,
            ["Policy"] = policy.Projections.ToString(),
            ["DdlAllowed"] = IsDdlAllowed(policy),
            ["MatchingMode"] = policy.Matching.ToString(),
            ["State"] = state
        };
        return Task.FromResult(report);
    }

    private Task EnsureCreatedJsonAsync<TEntity, TKey>(
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        string table,
        RelationalSchemaPolicy policy,
        CancellationToken ct)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        ct.ThrowIfCancellationRequested();
        var schema = ResolveSchema(policy);
        if (!ddl.TableExists(schema, table))
        {
            EnsureDdlAllowed(policy, features, schema, table);
            ddl.CreateTableIdJson(schema, table);
        }
        return Task.CompletedTask;
    }

    public Task EnsureCreatedAsync<TEntity, TKey>(
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        string table,
        RelationalSchemaPolicy policy,
        CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull
        => policy.Projections == RelationalProjectionMode.None
            ? EnsureCreatedJsonAsync<TEntity, TKey>(ddl, features, table, policy, ct)
            : EnsureCreatedMaterializedAsync<TEntity, TKey>(ddl, features, table, policy, ct);

    private Task EnsureCreatedMaterializedAsync<TEntity, TKey>(
        IRelationalDdlExecutor ddl,
        IRelationalStoreFeatures features,
        string table,
        RelationalSchemaPolicy policy,
        CancellationToken ct)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        ct.ThrowIfCancellationRequested();
        var entity = typeof(TEntity);
        var schema = ResolveSchema(policy);
        var projections = ProjectionResolver.Get(entity);
        var allColumns = new List<RelationalColumnDefinition>();

        // Always add Id and Json columns - use optimized storage type for Id
        var optimizationInfo = _sp.GetStorageOptimization<TEntity, TKey>();
        var idStorageType = GetIdStorageType<TKey>(optimizationInfo, features.ProviderName);

        allColumns.Add(new("Id", idStorageType, false));
        allColumns.Add(new("Json", typeof(string), false));
        foreach (var p in projections)
        {
            var clr = p.Property.PropertyType;
            var underlying = Nullable.GetUnderlyingType(clr) ?? clr;
            var nullable = Nullable.GetUnderlyingType(clr) is not null;
            if (features.SupportsJsonFunctions)
            {
                // Computed/generated from JSON; underlying nullability doesn't matter here
                allColumns.Add(new(p.ColumnName, typeof(string), true, true, "$." + p.Property.Name, p.IsIndexed));
            }
            else
            {
                // No JSON computed columns (e.g., SQLite): create physical columns but keep them nullable
                // so inserts that only write Json don’t violate NOT NULL constraints.
                allColumns.Add(new(p.ColumnName, underlying, true, false, null, p.IsIndexed));
            }
        }
        bool created = false;
        if (!ddl.TableExists(schema, table))
        {
            EnsureDdlAllowed(policy, features, schema, table);
            // Use the strongly-typed API on the executor. Executors that don't implement CreateTableWithColumns
            // should implement CreateTableIdJson instead; the interface now includes CreateTableWithColumns so
            // the MsSql executor implements it.
            ddl.CreateTableWithColumns(schema, table, allColumns);
            created = true;
        }
        // Only add missing columns if table already existed
        if (!created)
        {
            foreach (var col in allColumns.Skip(2)) // skip Id, Json (already created)
            {
                var exists = ddl.ColumnExists(schema, table, col.Name);
                if (!exists)
                {
                    EnsureDdlAllowed(policy, features, schema, table);
                    if (col.IsComputed && features.SupportsJsonFunctions)
                    {
                        // JsonPath is defined when IsComputed=true; assert non-null to satisfy nullable analysis
                        ddl.AddComputedColumnFromJson(schema, table, col.Name, col.JsonPath!, features.SupportsPersistedComputedColumns);
                    }
                    else
                    {
                        ddl.AddPhysicalColumn(schema, table, col.Name, col.ClrType, col.Nullable);
                    }
                }
            }
        }

        // JOBS-0008: create the declared indexes (per-column AND composite [Index] groups) for both freshly-created and
        // pre-existing tables. Previously per-column indexes were created only when ALTERing an existing table, so a
        // freshly-created table had NO secondary indexes — every filtered/ordered query full-scanned. This is what made
        // the per-lane claim seek slow on relational (no composite (Lane,Status,VisibleAt,FirstSubmittedAt) index).
        // CREATE INDEX IF NOT EXISTS makes it idempotent; each is best-effort so a non-indexable column degrades to a
        // scan, never a failure.
        if (IsDdlAllowed(policy))
            EnsureIndexes(ddl, features, schema, table, entity);
        return Task.CompletedTask;
    }

    private void EnsureIndexes(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, string schema, string table, Type entity)
    {
        if (!features.SupportsIndexesOnComputedColumns) return;
        foreach (var idx in IndexMetadata.GetIndexes(entity))
        {
            if (idx.IsPrimaryKey || idx.Ttl || idx.Properties.Count == 0) continue;   // PK is implicit; TTL is Mongo-only
            var cols = idx.Properties
                .Select(Koan.Data.Core.ProjectionResolver.ColumnNameOf)   // single converged column-name resolver (DATA-0105 §3a)
                .ToArray();
            var name = !string.IsNullOrWhiteSpace(idx.Name) ? idx.Name! : $"IX_{table}_{string.Join("_", cols)}";
            try { ddl.CreateIndex(schema, table, name, cols, unique: idx.Unique); }
            catch (Exception error)
            {
                _logger?.LogWarning(
                    error,
                    "Relational index {Index} could not be created for {Provider}/{Schema}/{Table}; queries may scan.",
                    name,
                    features.ProviderName,
                    schema,
                    table);
            }
        }
    }

    private static string ResolveSchema(RelationalSchemaPolicy policy)
        => string.IsNullOrWhiteSpace(policy.DefaultSchema) ? "dbo" : policy.DefaultSchema;


    private static string[] GetRequiredColumns(Type entity, RelationalSchemaPolicy policy)
    {
        var cols = new List<string> { "Id", "Json" };
        if (policy.Projections is RelationalProjectionMode.ComputedProjections or RelationalProjectionMode.PhysicalColumns)
        {
            var projections = ProjectionResolver.Get(entity);
            cols.AddRange(projections.Select(p => p.ColumnName));
        }
        return cols.Distinct(StringComparer.Ordinal).ToArray();
    }


    private static string ComputeState(bool tableExists, bool matches, RelationalSchemaPolicy policy)
    {
        if (!tableExists) return policy.Matching == RelationalSchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        if (!matches) return policy.Matching == RelationalSchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        return "Healthy";
    }

    private static void EnsureDdlAllowed(
        RelationalSchemaPolicy policy,
        IRelationalStoreFeatures features,
        string schema,
        string table)
    {
        if (!IsDdlAllowed(policy))
        {
            var reason = policy.Ddl != RelationalDdlPolicy.AutoCreate
                ? $"DDL is disabled by policy '{policy.Ddl}'."
                : "DDL is not allowed in production.";
            throw new InvalidOperationException(
                $"Relational schema creation was rejected for {features.ProviderName}/{schema}/{table}. {reason} " +
                "Set the selected provider's DdlPolicy to AutoCreate and explicitly allow production DDL only when intended.");
        }
    }

    private static bool IsDdlAllowed(RelationalSchemaPolicy policy)
        => policy.Ddl == RelationalDdlPolicy.AutoCreate && (!Koan.Core.KoanEnv.IsProduction || policy.AllowProductionDdl);

    private static Type GetIdStorageType<TKey>(StorageOptimizationInfo optimizationInfo, string providerName)
        where TKey : notnull
    {
        // For non-string keys, use the key type directly (no optimization needed)
        if (typeof(TKey) != typeof(string))
            return typeof(TKey);

        // For string keys, check if optimization is enabled
        if (!optimizationInfo.IsOptimized)
            return typeof(string);

        // Apply optimization based on type
        return optimizationInfo.OptimizationType switch
        {
            StorageOptimizationType.Guid => typeof(Guid),
            _ => typeof(string)
        };
    }

}
