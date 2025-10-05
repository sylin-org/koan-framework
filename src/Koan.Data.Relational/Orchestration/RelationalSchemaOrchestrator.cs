using Microsoft.Extensions.Options;
using Koan.Data.Core;
using System.Collections.Concurrent;
using System.Reflection;
using Koan.Data.Core.Optimization;

namespace Koan.Data.Relational.Orchestration;

internal sealed class RelationalSchemaOrchestrator : IRelationalSchemaOrchestrator
{
    private readonly IOptionsMonitor<RelationalMaterializationOptions> _optionsMonitor;
    private readonly IServiceProvider _sp;
    private readonly ConcurrentDictionary<string, (string State, string[] Missing, string[] Extra)> _cache = new(StringComparer.Ordinal);

    public RelationalSchemaOrchestrator(IOptionsMonitor<RelationalMaterializationOptions> optionsMonitor, IServiceProvider sp)
    { _optionsMonitor = optionsMonitor; _sp = sp; }

    public Task<object> ValidateAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        ct.ThrowIfCancellationRequested();
        var entity = typeof(TEntity);
        var (schema, table) = ResolveTable(entity);
        var required = GetRequiredColumns(entity, features);
        var missing = required.Where(c => !ddl.ColumnExists(schema, table, c)).ToArray();
        var state = ComputeState(ddl.TableExists(schema, table), missing.Length == 0);
        _cache[table] = (state, missing, Array.Empty<string>());
        var options = _optionsMonitor.CurrentValue;
        var report = new Dictionary<string, object?>
        {
            ["Provider"] = "relational",
            ["Table"] = table,
            ["TableExists"] = ddl.TableExists(schema, table),
            ["ProjectedColumns"] = required,
            ["MissingColumns"] = missing,
            ["Policy"] = options.Materialization.ToString(),
            ["DdlAllowed"] = IsDdlAllowed(options),
            ["MatchingMode"] = options.SchemaMatching.ToString(),
            ["State"] = state
        };
        return Task.FromResult<object>(report);
    }

    public Task EnsureCreatedJsonAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        ct.ThrowIfCancellationRequested();
        var entity = typeof(TEntity);
        var (schema, table) = ResolveTable(entity);
        if (!ddl.TableExists(schema, table))
        {
            EnsureDdlAllowed();
            ddl.CreateTableIdJson(schema, table);
        }
        return Task.CompletedTask;
    }

    public async Task EnsureCreatedAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        ct.ThrowIfCancellationRequested();
        // Precedence: [RelationalStorage(Shape=...)] > options.Materialization
        var entity = typeof(TEntity);
        var attr = entity.GetCustomAttribute<Abstractions.Annotations.RelationalStorageAttribute>(inherit: false);
        var options = _optionsMonitor.CurrentValue;
        System.Diagnostics.Debug.WriteLine($"[ORCH] EnsureCreatedAsync: Entity={entity.Name}, Materialization={options.Materialization}, DdlPolicy={options.DdlPolicy}, AllowProductionDdl={options.AllowProductionDdl}");
        if (attr is not null)
        {
            switch (attr.Shape)
            {
                case Abstractions.Annotations.RelationalStorageShape.Json:
                    System.Diagnostics.Debug.WriteLine($"[ORCH] EnsureCreatedAsync: Shape=Json, calling EnsureCreatedJsonAsync");
                    await EnsureCreatedJsonAsync<TEntity, TKey>(ddl, features, ct); return;
                case Abstractions.Annotations.RelationalStorageShape.ComputedProjections:
                case Abstractions.Annotations.RelationalStorageShape.PhysicalColumns:
                    System.Diagnostics.Debug.WriteLine($"[ORCH] EnsureCreatedAsync: Shape=Projections/Physical, calling EnsureCreatedMaterializedAsync");
                    await EnsureCreatedMaterializedAsync<TEntity, TKey>(ddl, features, ct); return;
            }
        }
        // Fallback to options
        if (options.Materialization == RelationalMaterializationPolicy.None)
        {
            System.Diagnostics.Debug.WriteLine($"[ORCH] EnsureCreatedAsync: Fallback to Json");
            await EnsureCreatedJsonAsync<TEntity, TKey>(ddl, features, ct);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[ORCH] EnsureCreatedAsync: Fallback to Materialized");
            await EnsureCreatedMaterializedAsync<TEntity, TKey>(ddl, features, ct);
        }
    }

    public Task EnsureCreatedMaterializedAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        ct.ThrowIfCancellationRequested();
        var entity = typeof(TEntity);
        var (schema, table) = ResolveTable(entity);
        System.Diagnostics.Debug.WriteLine($"[ORCH] EnsureCreatedMaterializedAsync: Entity={entity.Name}, Schema={schema}, Table={table}");
        var projections = ProjectionResolver.Get(entity);
        var allColumns = new List<(string Name, Type ClrType, bool Nullable, bool IsComputed, string? JsonPath, bool IsIndexed)>();

        // Always add Id and Json columns - use optimized storage type for Id
        var optimizationInfo = _sp.GetStorageOptimization<TEntity, TKey>();
        var idStorageType = GetIdStorageType<TKey>(optimizationInfo, features.ProviderName);

        System.Diagnostics.Debug.WriteLine($"[ORCH] ID Storage Optimization: Entity={entity.Name}, Provider={features.ProviderName}, " +
            $"OptimizationType={optimizationInfo.OptimizationType}, StorageType={idStorageType.Name}, " +
            $"IsOptimized={optimizationInfo.IsOptimized}");

        allColumns.Add(("Id", idStorageType, false, false, null, false));
        allColumns.Add(("Json", typeof(string), false, false, null, false));
        foreach (var p in projections)
        {
            var clr = p.Property.PropertyType;
            var underlying = Nullable.GetUnderlyingType(clr) ?? clr;
            var nullable = Nullable.GetUnderlyingType(clr) is not null;
            if (features.SupportsJsonFunctions)
            {
                // Computed/generated from JSON; underlying nullability doesn't matter here
                allColumns.Add((p.ColumnName, typeof(string), true, true, "$." + p.Property.Name, p.IsIndexed));
            }
            else
            {
                // No JSON computed columns (e.g., SQLite): create physical columns but keep them nullable
                // so inserts that only write Json don’t violate NOT NULL constraints.
                allColumns.Add((p.ColumnName, underlying, true, false, null, p.IsIndexed));
            }
        }
        bool created = false;
        if (!ddl.TableExists(schema, table))
        {
            System.Diagnostics.Debug.WriteLine($"[ORCH] Table does not exist, calling EnsureDdlAllowed and CreateTableWithColumns");
            EnsureDdlAllowed();
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
                System.Diagnostics.Debug.WriteLine($"[ORCH] Checking column: {col.Name}");
                var exists = ddl.ColumnExists(schema, table, col.Name);
                System.Diagnostics.Debug.WriteLine($"[ORCH] ColumnExists({col.Name}) returned: {exists}");
                if (!exists)
                {
                    System.Diagnostics.Debug.WriteLine($"[ORCH] Column {col.Name} does not exist, will add");
                    if (col.IsComputed && features.SupportsJsonFunctions)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ORCH] Adding computed column from JSON: {col.Name}, path={col.JsonPath}");
                        // JsonPath is defined when IsComputed=true; assert non-null to satisfy nullable analysis
                        ddl.AddComputedColumnFromJson(schema, table, col.Name, col.JsonPath!, features.SupportsPersistedComputedColumns);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ORCH] Adding physical column: {col.Name}, type={col.ClrType}, nullable={col.Nullable}");
                        ddl.AddPhysicalColumn(schema, table, col.Name, col.ClrType, col.Nullable);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ORCH] Column {col.Name} already exists");
                }
                if (col.IsIndexed && features.SupportsIndexesOnComputedColumns)
                {
                    var ixName = $"IX_{table}_{col.Name}";
                    System.Diagnostics.Debug.WriteLine($"[ORCH] Creating index: {ixName} on {col.Name}");
                    ddl.CreateIndex(schema, table, ixName, new[] { col.Name }, unique: false);
                }
            }
        }
        return Task.CompletedTask;
    }

    private (string schema, string table) ResolveTable(Type entity)
    {
        // Resolve via StorageNameResolver defaults for relational. Allow provider-specific schema overrides via options.
        var schema = _optionsMonitor.CurrentValue.DefaultSchema;
        if (string.IsNullOrWhiteSpace(schema)) schema = "dbo";
        var method = typeof(Core.Configuration.StorageNameRegistry).GetMethods()
            .First(m => m.Name == "GetOrCompute" && m.GetGenericArguments().Length == 2);

        // Extract the actual TKey type from the entity's IEntity<TKey> interface
        var entityInterface = entity.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Abstractions.IEntity<>));

        if (entityInterface == null)
            throw new InvalidOperationException($"Entity type {entity.Name} does not implement IEntity<TKey>");

        var keyType = entityInterface.GetGenericArguments()[0];
        var table = (string)method.MakeGenericMethod(entity, keyType).Invoke(null, new object?[] { _sp })!;
        return (schema, table);
    }


    private string[] GetRequiredColumns(Type entity, IRelationalStoreFeatures features)
    {
        var options = _optionsMonitor.CurrentValue;
        var cols = new List<string> { "Id", "Json" };
        if (options.Materialization == RelationalMaterializationPolicy.ComputedProjections || options.Materialization == RelationalMaterializationPolicy.PhysicalColumns)
        {
            var projections = ProjectionResolver.Get(entity);
            cols.AddRange(projections.Select(p => p.ColumnName));
        }
        return cols.Distinct(StringComparer.Ordinal).ToArray();
    }


    private string ComputeState(bool tableExists, bool matches)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!tableExists) return options.SchemaMatching == RelationalSchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        if (!matches) return options.SchemaMatching == RelationalSchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        return "Healthy";
    }


    private void EnsureDdlAllowed()
    {
        var options = _optionsMonitor.CurrentValue;
        if (!IsDdlAllowed(options))
        {
            var reason = options.DdlPolicy != RelationalDdlPolicy.AutoCreate
                ? "DDL is disabled by policy."
                : "DDL not allowed in production.";
            throw new InvalidOperationException(reason);
        }
    }

    private static bool IsDdlAllowed(RelationalMaterializationOptions options)
        => options.DdlPolicy == RelationalDdlPolicy.AutoCreate && (!Koan.Core.KoanEnv.IsProduction || options.AllowProductionDdl);

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
