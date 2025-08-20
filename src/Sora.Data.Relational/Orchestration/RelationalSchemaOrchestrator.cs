using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.Core.Metadata;
using Sora.Data.Relational.Orchestration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Data.Relational.Orchestration;

internal sealed class RelationalSchemaOrchestrator : IRelationalSchemaOrchestrator
{
    private readonly RelationalMaterializationOptions _options;
    private readonly IServiceProvider _sp;
    private readonly ConcurrentDictionary<string, (string State, string[] Missing, string[] Extra)> _cache = new(StringComparer.Ordinal);

    public RelationalSchemaOrchestrator(IOptions<RelationalMaterializationOptions> options, IServiceProvider sp)
    { _options = options.Value; _sp = sp; }

    public Task<object> ValidateAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Sora.Data.Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        ct.ThrowIfCancellationRequested();
        var entity = typeof(TEntity);
        var (schema, table) = ResolveTable(entity);
        var required = GetRequiredColumns(entity, features);
        var missing = required.Where(c => !ddl.ColumnExists(schema, table, c)).ToArray();
        var state = ComputeState(ddl.TableExists(schema, table), missing.Length == 0);
        _cache[table] = (state, missing, Array.Empty<string>());
        var report = new Dictionary<string, object?>
        {
            ["Provider"] = "relational",
            ["Table"] = table,
            ["TableExists"] = ddl.TableExists(schema, table),
            ["ProjectedColumns"] = required,
            ["MissingColumns"] = missing,
            ["Policy"] = _options.Materialization.ToString(),
            ["DdlAllowed"] = IsDdlAllowed(),
            ["MatchingMode"] = _options.SchemaMatching.ToString(),
            ["State"] = state
        };
        return Task.FromResult<object>(report);
    }

    public Task EnsureCreatedJsonAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Sora.Data.Abstractions.IEntity<TKey>
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
        where TEntity : class, Sora.Data.Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        ct.ThrowIfCancellationRequested();
        // Precedence: [RelationalStorage(Shape=...)] > options.Materialization
        var entity = typeof(TEntity);
        var attr = entity.GetCustomAttribute<Sora.Data.Abstractions.Annotations.RelationalStorageAttribute>(inherit: false);
        if (attr is not null)
        {
            switch (attr.Shape)
            {
                case Sora.Data.Abstractions.Annotations.RelationalStorageShape.Json:
                    await EnsureCreatedJsonAsync<TEntity, TKey>(ddl, features, ct); return;
                case Sora.Data.Abstractions.Annotations.RelationalStorageShape.ComputedProjections:
                case Sora.Data.Abstractions.Annotations.RelationalStorageShape.PhysicalColumns:
                    await EnsureCreatedMaterializedAsync<TEntity, TKey>(ddl, features, ct); return;
            }
        }
        // Fallback to options
        if (_options.Materialization == RelationalMaterializationPolicy.None)
            await EnsureCreatedJsonAsync<TEntity, TKey>(ddl, features, ct);
        else
            await EnsureCreatedMaterializedAsync<TEntity, TKey>(ddl, features, ct);
    }

    public Task EnsureCreatedMaterializedAsync<TEntity, TKey>(IRelationalDdlExecutor ddl, IRelationalStoreFeatures features, CancellationToken ct = default)
        where TEntity : class, Sora.Data.Abstractions.IEntity<TKey>
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
        var projections = ProjectionResolver.Get(entity);
        foreach (var p in projections)
        {
            var col = p.ColumnName;
            if (!ddl.ColumnExists(schema, table, col))
            {
                if (features.SupportsJsonFunctions)
                {
                    var path = "$." + p.Property.Name;
                    ddl.AddComputedColumnFromJson(schema, table, col, path, features.SupportsPersistedComputedColumns);
                }
                else
                {
                    var clr = p.Property.PropertyType;
                    var underlying = Nullable.GetUnderlyingType(clr) ?? clr;
                    var nullable = Nullable.GetUnderlyingType(clr) is not null;
                    ddl.AddPhysicalColumn(schema, table, col, underlying, nullable);
                }
            }
            if (p.IsIndexed && features.SupportsIndexesOnComputedColumns)
            {
                var ixName = $"IX_{table}_{col}";
                ddl.CreateIndex(schema, table, ixName, new[] { col }, unique: false);
            }
        }
        return Task.CompletedTask;
    }

    private (string schema, string table) ResolveTable(Type entity)
    {
        // Resolve via StorageNameResolver defaults for relational: dbo schema + registered physical name
        var schema = "dbo";
        var method = typeof(Sora.Data.Core.Configuration.StorageNameRegistry).GetMethods()
            .First(m => m.Name == "GetOrCompute" && m.GetGenericArguments().Length == 2);
        // Fallback: try to use string key if reflection fails (shouldn't in our codebase)
        var table = (string)method.MakeGenericMethod(entity, typeof(string)).Invoke(null, new object?[] { _sp })!;
        return (schema, table);
    }

    private string[] GetRequiredColumns(Type entity, IRelationalStoreFeatures features)
    {
        var cols = new List<string> { "Id", "Json" };
        if (_options.Materialization == RelationalMaterializationPolicy.ComputedProjections || _options.Materialization == RelationalMaterializationPolicy.PhysicalColumns)
        {
            var projections = ProjectionResolver.Get(entity);
            cols.AddRange(projections.Select(p => p.ColumnName));
        }
        return cols.Distinct(StringComparer.Ordinal).ToArray();
    }

    private string ComputeState(bool tableExists, bool matches)
    {
        if (!tableExists) return _options.SchemaMatching == RelationalSchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        if (!matches) return _options.SchemaMatching == RelationalSchemaMatchingMode.Strict ? "Unhealthy" : "Degraded";
        return "Healthy";
    }

    private void EnsureDdlAllowed()
    {
        if (_options.DdlPolicy != RelationalDdlPolicy.AutoCreate) throw new InvalidOperationException("DDL is disabled by policy.");
        if (Sora.Core.SoraEnv.IsProduction && !_options.AllowProductionDdl) throw new InvalidOperationException("DDL not allowed in production.");
    }

    private bool IsDdlAllowed()
        => _options.DdlPolicy == RelationalDdlPolicy.AutoCreate && (!Sora.Core.SoraEnv.IsProduction || _options.AllowProductionDdl);
}
