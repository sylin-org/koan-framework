using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Adapters;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Koan.Data.Core.Document;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Sorting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo;

/// <summary>
/// The MongoDB document adapter — the golden realization of the <see cref="DocumentStore{TEntity,TKey}"/> family
/// (ARCH-0103 P3, rebuilt from the document-store catalogue). The base owns the readiness-gated + traced op-template,
/// the AODB managed-write composition, the schema-ready gate, and the batch / instruction skeletons; this dialect
/// supplies only the native MongoDB primitives over <see cref="IMongoCollection{TEntity}"/> and announces its native
/// extras (bulk · atomic · CAS · TTL · fast-remove).
///
/// <para><b>Harvested intact</b> (not rewritten — these encode hard-won correctness): <see cref="MongoFilterTranslator{TEntity}"/>
/// (DATA-0098 comparand-through-the-field's-serializer), <c>MongoGuidEncoding</c> + the global BSON conventions
/// (GUID↔BinData single source of truth, comparable encoding DATA-0100, no-discriminator), and the
/// <see cref="BuildIndexModels"/> TTL semantics (DATA-0101).</para>
/// </summary>
internal sealed class MongoDocumentStore<TEntity, TKey> :
    DocumentStore<TEntity, TKey>,
    IConditionalWriteRepository<TEntity, TKey>,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly MongoClientProvider _provider;
    private readonly IOptionsMonitor<MongoOptions> _options;
    private readonly IServiceProvider _sp;
    private readonly string _source;
    private readonly ILogger? _logger;
    private readonly StorageOptimizationInfo _optimizationInfo;
    private readonly MongoFilterTranslator<TEntity> _translator;
    // Collection handles resolved per-(ambient partition) and cached by their resolved name; never a single shared
    // mutable "current collection" field (concurrent partitions would cross). The schema-ready gate (base.Schema)
    // ensures-once per collection key.
    private readonly ConcurrentDictionary<string, IMongoCollection<TEntity>> _collections = new(StringComparer.Ordinal);

    public MongoDocumentStore(MongoClientProvider provider, IOptionsMonitor<MongoOptions> options, IServiceProvider sp, string source)
    {
        _provider = provider;
        _options = options;
        _sp = sp;
        _source = source;
        _logger = sp.GetService<ILogger<MongoDocumentStore<TEntity, TKey>>>();
        _optimizationInfo = sp.GetStorageOptimization<TEntity, TKey>();
        _translator = new MongoFilterTranslator<TEntity>(MapFieldName);
    }

    public StorageOptimizationInfo OptimizationInfo => _optimizationInfo;

    // ==================== The family seam: identity, telemetry, readiness ====================

    protected override IAdapterReadiness Readiness => _provider;
    protected override ActivitySource Telemetry => MongoTelemetry.Activity;
    protected override string Verb => "mongo";
    protected override string? RoutedSource => string.Equals(_source, "Default", StringComparison.OrdinalIgnoreCase) ? null : _source;

    public override ReadinessPolicy Policy => _options.CurrentValue.Readiness.Policy;
    public override TimeSpan Timeout
    {
        get { var t = _options.CurrentValue.Readiness.Timeout; return t > TimeSpan.Zero ? t : _provider.ReadinessTimeout; }
    }
    public override bool EnableReadinessGating => _options.CurrentValue.Readiness.EnableReadinessGating;

    protected override void DescribeBackend(ICapabilities caps) => caps
        .Add(DataCaps.Write.BulkUpsert).Add(DataCaps.Write.BulkDelete)
        .Add(DataCaps.Write.AtomicBatch).Add(DataCaps.Write.FastRemove)
        .Add(DataCaps.Write.ConditionalReplace)
        .Add(DataCaps.Retention.TtlIndex)
        .Add(DataCaps.Query.FilterExecution, new FilterExecutionProfile(FilterExecutionKind.Native))
        .Add(DataCaps.Query.Filter, MongoFilterTranslator<TEntity>.Capabilities);

    protected override RemoveStrategy ResolveStrategy(RemoveStrategy strategy)
        => strategy == RemoveStrategy.Optimized ? RemoveStrategy.Fast : strategy;   // this adapter declares FastRemove

    // ==================== Container resolution + schema (Container mode) ====================

    private string CollectionName()
    {
        var sp = Koan.Core.Hosting.App.AppHost.Current ?? _sp;
        return AdapterNaming.GetOrCompute<TEntity, TKey>(sp);
    }

    private string CollectionKey(string name) => $"{_source}|{_options.CurrentValue.Database}|{name}";

    private async Task<IMongoCollection<TEntity>> GetCollectionAsync(CancellationToken ct)
    {
        await EnsureContainerAsync(ct).ConfigureAwait(false);   // connects the provider + ensures the schema (once)
        var name = CollectionName();
        if (_collections.TryGetValue(name, out var cached)) return cached;
        var database = await _provider.GetDatabase(ct).ConfigureAwait(false);
        return _collections.GetOrAdd(name, n => database.GetCollection<TEntity>(n));
    }

    // Idempotent (one run per container key via the base's schema gate). UNGATED on readiness: the first call here is
    // what connects the provider (GetDatabase transitions Initializing→Ready), so it cannot wait on readiness first.
    protected override Task EnsureContainerAsync(CancellationToken ct)
    {
        var name = CollectionName();
        return Schema.RunOnceAsync(CollectionKey(name), () => EnsureCollectionAsync(name, ct), ct);
    }

    private async Task EnsureCollectionAsync(string name, CancellationToken ct)
    {
        var database = await _provider.GetDatabase(ct).ConfigureAwait(false);
        if (!await CollectionExists(database, name, ct).ConfigureAwait(false))
        {
            try { await database.CreateCollectionAsync(name, cancellationToken: ct).ConfigureAwait(false); }
            catch (MongoCommandException ex) when (ex.CodeName == "NamespaceExists") { /* raced — fine */ }
        }
        var collection = _collections.GetOrAdd(name, n => database.GetCollection<TEntity>(n));
        await EnsureIndexes(collection, ct).ConfigureAwait(false);
    }

    private static async Task<bool> CollectionExists(IMongoDatabase database, string name, CancellationToken ct)
    {
        var options = new ListCollectionNamesOptions { Filter = new BsonDocument("name", name) };
        using var cursor = await database.ListCollectionNamesAsync(options, ct).ConfigureAwait(false);
        return await cursor.AnyAsync(ct).ConfigureAwait(false);
    }

    // [Index]/TTL → index models. Field names traverse the SAME camelCase + _id map as filters/sorts (JOBS-0008): the
    // string Ascending(string) overload takes a LITERAL name and does NOT apply the camelCase convention, so building
    // keys from the raw PascalCase property leaves the index uncovered.
    private IReadOnlyList<CreateIndexModel<TEntity>> BuildIndexModels()
    {
        var models = new List<CreateIndexModel<TEntity>>();
        var keysBuilder = Builders<TEntity>.IndexKeys;
        foreach (var idx in IndexMetadata.GetIndexes(typeof(TEntity)))
        {
            if (idx.IsPrimaryKey || idx.Properties.Count == 0) continue;
            IndexKeysDefinition<TEntity>? keys = null;
            foreach (var property in idx.Properties)
            {
                var field = keysBuilder.Ascending(MapFieldName(property.Name));
                keys = keys is null ? field : keysBuilder.Combine(keys, field);
            }
            if (keys is null) continue;
            var name = !string.IsNullOrWhiteSpace(idx.Name) ? idx.Name! : $"ix_{string.Join("_", idx.Properties.Select(p => p.Name))}";
            var options = new CreateIndexOptions { Name = name, Unique = idx.Unique };
            // DATA-0101 TTL: a single-field [Index(Ttl=true)] timestamp expires each row once its value is in the past
            // (expireAfterSeconds = 0). A null/absent value is never expired.
            if (idx.Ttl && idx.Properties.Count == 1) options.ExpireAfter = TimeSpan.Zero;
            models.Add(new CreateIndexModel<TEntity>(keys, options));
        }
        return models;
    }

    private async Task EnsureIndexes(IMongoCollection<TEntity> collection, CancellationToken ct)
    {
        var models = BuildIndexModels();
        if (models.Count == 0) return;
        try { await collection.Indexes.CreateManyAsync(models, cancellationToken: ct).ConfigureAwait(false); }
        catch (MongoCommandException ex) when (ex.CodeName is "IndexOptionsConflict" or "IndexKeySpecsConflict" or "IndexAlreadyExists") { /* idempotent */ }
        catch (Exception ex) { _logger?.LogDebug(ex, "Mongo index ensure failed for {Collection}", collection.CollectionNamespace.CollectionName); }
    }

    // ==================== Read ====================

    protected override async Task<TEntity?> FindByIdAsync(TKey id, CancellationToken ct)
    {
        var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
        return await collection.Find(Builders<TEntity>.Filter.Eq(x => x.Id, id)).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    protected override async Task<IReadOnlyList<TEntity?>> FindManyAsync(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
        var found = await collection.Find(Builders<TEntity>.Filter.In(x => x.Id, ids)).ToListAsync(ct).ConfigureAwait(false);
        var map = found.ToDictionary(e => e.Id);
        var results = new TEntity?[ids.Count];
        for (var i = 0; i < ids.Count; i++) results[i] = map.TryGetValue(ids[i], out var e) ? e : null;
        return results;
    }

    protected override async Task<RepositoryQueryResult<TEntity>> QueryNativeAsync(QueryDefinition query, CancellationToken ct)
    {
        var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
        var filter = BuildFilter(query.Filter);
        var (sortDef, sortHandled) = BuildSort(query.Sort);
        var totalCount = await collection.CountDocumentsAsync(filter, cancellationToken: ct).ConfigureAwait(false);

        var cursor = collection.Find(filter);
        if (sortDef is not null) cursor = cursor.Sort(sortDef);

        var paginationHandled = false;
        if (query.HasPagination)
        {
            var skip = (query.EffectivePage() - 1) * query.EffectivePageSize();
            cursor = cursor.Skip(skip).Limit(query.EffectivePageSize());
            paginationHandled = true;
        }

        var results = await cursor.ToListAsync(ct).ConfigureAwait(false);
        return new RepositoryQueryResult<TEntity>
        {
            Items = results,
            TotalCount = totalCount,
            IsEstimate = false,
            SortHandled = sortHandled,
            PaginationHandled = paginationHandled,
        };
    }

    protected override async Task<CountResult> CountNativeAsync(QueryDefinition query, CancellationToken ct)
    {
        var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
        if (query.CountStrategy == CountStrategy.Fast && query.Filter is null)
            return CountResult.Estimate((long)await collection.EstimatedDocumentCountAsync(cancellationToken: ct).ConfigureAwait(false));
        var count = await collection.CountDocumentsAsync(BuildFilter(query.Filter), cancellationToken: ct).ConfigureAwait(false);
        return CountResult.Exact(count);
    }

    private FilterDefinition<TEntity> BuildFilter(Filter? filter)
        => filter is null ? Builders<TEntity>.Filter.Empty : _translator.Translate(filter, typeof(TEntity));

    private (SortDefinition<TEntity>? Def, IReadOnlySet<SortSpec> Handled) BuildSort(IReadOnlyList<SortSpec> specs)
    {
        if (specs.Count == 0) return (null, RepositoryQueryResult<TEntity>.NoSortHandled);
        var keys = Builders<TEntity>.Sort;
        SortDefinition<TEntity>? def = null;
        foreach (var spec in specs)
        {
            var name = string.Join('.', spec.Path.Members.Select((m, i) => i == 0 ? MapFieldName(m.Name) : ToCamelCase(m.Name)));
            var part = spec.Desc ? keys.Descending(name) : keys.Ascending(name);
            def = def is null ? part : keys.Combine(def, part);
        }
        return (def, specs.ToFrozenSet());
    }

    // ==================== Write (Shared mode) ====================

    protected override async Task UpsertOneNativeAsync(TEntity model, IReadOnlyDictionary<string, object?>? inject, IReadOnlyDictionary<string, object?>? guard, CancellationToken ct)
    {
        var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
        if (inject is null || inject.Count == 0)
        {
            await collection.ReplaceOneAsync(Builders<TEntity>.Filter.Eq(x => x.Id, model.Id), model, new ReplaceOptions { IsUpsert = true }, ct).ConfigureAwait(false);
            return;
        }
        await ManagedUpsertOneAsync(collection, model, inject, guard, ct).ConfigureAwait(false);
    }

    protected override async Task<int> UpsertManyNativeAsync(IReadOnlyList<TEntity> models, IReadOnlyDictionary<string, object?>? inject, IReadOnlyDictionary<string, object?>? guard, CancellationToken ct)
    {
        if (models.Count == 0) return 0;
        var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
        if (inject is not null && inject.Count > 0)
        {
            // Under a managed scope, replace per document (the conflict-aware path); inject Effective, guard Current.
            foreach (var model in models) { ct.ThrowIfCancellationRequested(); await ManagedUpsertOneAsync(collection, model, inject, guard, ct).ConfigureAwait(false); }
            return models.Count;
        }
        var writes = models.Select(m => new ReplaceOneModel<TEntity>(Builders<TEntity>.Filter.Eq(x => x.Id, m.Id), m) { IsUpsert = true }).ToArray();
        var result = await collection.BulkWriteAsync(writes, cancellationToken: ct).ConfigureAwait(false);
        return (int)(result.ModifiedCount + result.Upserts.Count);
    }

    // Managed-field conflict-aware upsert (DATA-0105 §3b). Serialize to a BsonDocument, inject the managed elements
    // (Effective), and replace through a BsonDocument view with a conflict-aware filter {_id, <guard eqs>} built from
    // the ISOLATION values only (Current). A foreign-owned doc fails the filter → INSERT same _id → E11000 → reject.
    private static async Task ManagedUpsertOneAsync(IMongoCollection<TEntity> collection, TEntity model, IReadOnlyDictionary<string, object?> inject, IReadOnlyDictionary<string, object?>? guard, CancellationToken ct)
    {
        var doc = model.ToBsonDocument();
        foreach (var kv in inject) doc[kv.Key] = ToBson(kv.Value);

        var docs = collection.Database.GetCollection<BsonDocument>(collection.CollectionNamespace.CollectionName);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]);
        if (guard is not null)
            foreach (var kv in guard)
                filter = Builders<BsonDocument>.Filter.And(filter, Builders<BsonDocument>.Filter.Eq(kv.Key, ToBson(kv.Value)));

        try
        {
            await docs.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, ct).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw CrossScopeWrite(collection.CollectionNamespace.CollectionName, doc["_id"].ToString());
        }
    }

    private static BsonValue ToBson(object? value) => value is null ? BsonNull.Value : BsonValue.Create(value);

    protected override async Task<bool> DeleteOneNativeAsync(TKey id, CancellationToken ct)
    {
        var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
        var result = await collection.DeleteOneAsync(Builders<TEntity>.Filter.Eq(x => x.Id, id), ct).ConfigureAwait(false);
        return result.DeletedCount > 0;
    }

    protected override async Task<int> DeleteManyNativeAsync(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
        var result = await collection.DeleteManyAsync(Builders<TEntity>.Filter.In(x => x.Id, ids), ct).ConfigureAwait(false);
        return (int)result.DeletedCount;
    }

    protected override async Task<long> ClearNativeAsync(RemoveStrategy strategy, CancellationToken ct)
    {
        var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
        if (strategy == RemoveStrategy.Fast)
        {
            // Drop & recreate (loses indexes briefly — re-ensured below; invalidate the schema gate so the next op re-runs it).
            var database = collection.Database;
            var name = collection.CollectionNamespace.CollectionName;
            var estimated = await collection.EstimatedDocumentCountAsync(cancellationToken: ct).ConfigureAwait(false);
            await database.DropCollectionAsync(name, ct).ConfigureAwait(false);
            await database.CreateCollectionAsync(name, cancellationToken: ct).ConfigureAwait(false);
            var recreated = database.GetCollection<TEntity>(name);
            _collections[name] = recreated;
            await EnsureIndexes(recreated, ct).ConfigureAwait(false);
            Schema.Invalidate(CollectionKey(name));
            return estimated;
        }
        var result = await collection.DeleteManyAsync(Builders<TEntity>.Filter.Empty, ct).ConfigureAwait(false);
        return result.DeletedCount;
    }

    protected override async Task<BatchResult> SaveBatchNativeAsync(IReadOnlyList<TEntity> upserts, IReadOnlyList<TKey> deletes, bool requireAtomic, CancellationToken ct)
    {
        var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
        var ops = new List<WriteModel<TEntity>>(upserts.Count + deletes.Count);
        foreach (var e in upserts) ops.Add(new ReplaceOneModel<TEntity>(Builders<TEntity>.Filter.Eq(x => x.Id, e.Id), e) { IsUpsert = true });
        foreach (var id in deletes) ops.Add(new DeleteOneModel<TEntity>(Builders<TEntity>.Filter.Eq(x => x.Id, id)));
        if (ops.Count == 0) return new BatchResult(0, 0, 0);

        if (!requireAtomic)
        {
            var r = await collection.BulkWriteAsync(ops, cancellationToken: ct).ConfigureAwait(false);
            return new BatchResult(r.Upserts.Count, (int)r.ModifiedCount, (int)r.DeletedCount);
        }

        using var session = await collection.Database.Client.StartSessionAsync(cancellationToken: ct).ConfigureAwait(false);
        try { session.StartTransaction(); }
        catch (MongoClientException ex) when (ex.Message.Contains("Transactions are not supported", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("MongoDB deployment does not support transactions; cannot honor RequireAtomic=true.", ex);
        }
        try
        {
            var r = await collection.BulkWriteAsync(session, ops, cancellationToken: ct).ConfigureAwait(false);
            await session.CommitTransactionAsync(ct).ConfigureAwait(false);
            return new BatchResult(r.Upserts.Count, (int)r.ModifiedCount, (int)r.DeletedCount);
        }
        catch
        {
            try { await session.AbortTransactionAsync(ct).ConfigureAwait(false); } catch { /* swallow abort failures */ }
            throw;
        }
    }

    // ==================== Native CAS (IConditionalWriteRepository) ====================

    /// <summary>Atomic CAS (JOBS-0005 §20.3): a single-document ReplaceOne whose filter is <c>_id == model.Id</c> AND the
    /// lowered <paramref name="guard"/> — naturally atomic, no transaction. Modified = applied, 0 = lost.</summary>
    public Task<bool> ConditionalReplaceAsync(TEntity model, Expression<Func<TEntity, bool>> guard, CancellationToken ct = default)
        => RunAsync("conditional-replace", async () =>
        {
            var collection = await GetCollectionAsync(ct).ConfigureAwait(false);
            var guardFilter = _translator.Translate(LinqFilterCompiler.Compile(guard), typeof(TEntity));
            var filter = Builders<TEntity>.Filter.And(Builders<TEntity>.Filter.Eq(x => x.Id, model.Id), guardFilter);
            var result = await collection.ReplaceOneAsync(filter, model, new ReplaceOptions { IsUpsert = false }, ct).ConfigureAwait(false);
            return result.IsAcknowledged && result.ModifiedCount > 0;
        }, ct);

    // ==================== Field-name mapping (one helper; _id carve-out) ====================

    private string MapFieldName(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName)) return propertyName;
        if (!string.IsNullOrWhiteSpace(_optimizationInfo.IdPropertyName) && string.Equals(propertyName, _optimizationInfo.IdPropertyName, StringComparison.Ordinal))
            return "_id";
        if (string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase)) return "_id";
        return ToCamelCase(propertyName);
    }

    private static string ToCamelCase(string value)
        => string.IsNullOrEmpty(value) || !char.IsUpper(value[0]) ? value
           : value.Length == 1 ? value.ToLowerInvariant() : char.ToLowerInvariant(value[0]) + value.Substring(1);
}
