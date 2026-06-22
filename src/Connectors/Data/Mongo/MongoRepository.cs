using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Adapters;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Koan.Data.Core.Extensions;
using Koan.Data.Core.Optimization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Collections.Frozen;

namespace Koan.Data.Connector.Mongo;

internal sealed class MongoRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IDescribesCapabilities,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>,
    IConditionalWriteRepository<TEntity, TKey>,
    IInstructionExecutor<TEntity>,
    IAdapterReadiness,
    IAdapterReadinessConfiguration
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly MongoClientProvider _provider;
    private readonly IOptionsMonitor<MongoOptions> _options;
    private readonly IServiceProvider _sp;
    private readonly ILogger? _logger;
    private readonly StorageOptimizationInfo _optimizationInfo;
    // Collections are resolved per-operation from the ambient partition and cached by their resolved name.
    // A single shared mutable "current collection" field would race across concurrent partitions: this
    // repository is process-wide-cached by DataService (a singleton) with a cache key that omits the
    // partition, so concurrent flows under different partitions share one instance — a shared field would
    // let one flow's write land in another partition's collection.
    private readonly ConcurrentDictionary<string, IMongoCollection<TEntity>> _collections = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, bool> _healthyCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, bool> _indexCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _schemaLocks = new(StringComparer.Ordinal);

    public MongoRepository(
        MongoClientProvider provider,
        IOptionsMonitor<MongoOptions> options,
        IStorageNameResolver nameResolver,
        IServiceProvider sp)
    {
        _provider = provider;
        _options = options;
        _sp = sp;
        _ = nameResolver; // ensure resolver is materialized for convention overrides
        _logger = sp.GetService<ILogger<MongoRepository<TEntity, TKey>>>();
        _optimizationInfo = sp.GetStorageOptimization<TEntity, TKey>();
    }

    public void Describe(ICapabilities caps) => caps
        .Add(DataCaps.Query.Linq)
        .Add(DataCaps.Write.BulkUpsert).Add(DataCaps.Write.BulkDelete)
        .Add(DataCaps.Write.AtomicBatch).Add(DataCaps.Write.FastRemove)
        .Add(DataCaps.Write.ConditionalReplace)
        .Add(DataCaps.Retention.TtlIndex)
        // Row-isolation (DATA-0105 §3b): injects a framework-managed discriminator as a BSON element and pushes
        // scalar equality on it (the shared FieldPathResolver + IgnoreExtraElements), with a conflict-aware upsert.
        .Add(DataCaps.Isolation.RowScoped)
        .Add(DataCaps.Query.Filter, MongoFilterTranslator<TEntity>.Capabilities);
    public StorageOptimizationInfo OptimizationInfo => _optimizationInfo;


    private MongoFilterTranslator<TEntity> Translator => new(MapFieldName);

    /// <summary>Atomic CAS (JOBS-0005 §20.3): a single-document ReplaceOne whose filter is <c>_id == model.Id</c> AND
    /// the lowered <paramref name="guard"/> — naturally atomic, no transaction. Modified = applied, 0 = lost.</summary>
    public Task<bool> ConditionalReplaceAsync(TEntity model, Expression<Func<TEntity, bool>> guard, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            var collection = await GetCollection(ct).ConfigureAwait(false);
            var guardFilter = Translator.Translate(LinqFilterCompiler.Compile(guard), typeof(TEntity));
            var filter = Builders<TEntity>.Filter.And(Builders<TEntity>.Filter.Eq(x => x.Id, model.Id), guardFilter);
            var result = await collection.ReplaceOneAsync(filter, model, new ReplaceOptions { IsUpsert = false }, ct).ConfigureAwait(false);
            return result.IsAcknowledged && result.ModifiedCount > 0;
        }, ct);

    public AdapterReadinessState ReadinessState => _provider.ReadinessState;
    public bool IsReady => _provider.IsReady;
    public TimeSpan ReadinessTimeout => _provider.ReadinessTimeout;

    public event EventHandler<ReadinessStateChangedEventArgs>? ReadinessStateChanged
    {
        add => _provider.ReadinessStateChanged += value;
        remove => _provider.ReadinessStateChanged -= value;
    }

    public ReadinessStateManager StateManager => _provider.StateManager;

    public Task<bool> IsReadyAsync(CancellationToken ct = default) => _provider.IsReadyAsync(ct);

    public Task WaitForReadiness(TimeSpan? timeout = null, CancellationToken ct = default)
        => _provider.WaitForReadiness(timeout ?? Timeout, ct);

    public ReadinessPolicy Policy => _options.CurrentValue.Readiness.Policy;

    public TimeSpan Timeout
    {
        get
        {
            var timeout = _options.CurrentValue.Readiness.Timeout;
            return timeout > TimeSpan.Zero ? timeout : _provider.ReadinessTimeout;
        }
    }

    public bool EnableReadinessGating => _options.CurrentValue.Readiness.EnableReadinessGating;

    private Task<TResult> ExecuteWithReadinessAsync<TResult>(Func<Task<TResult>> operation, CancellationToken ct)
        => this.WithReadinessAsync<TResult, TEntity>(operation, ct);

    private Task ExecuteWithReadiness(Func<Task> operation, CancellationToken ct)
        => this.WithReadiness(operation, ct);

    internal Task<TResult> ExecuteWithinReadinessAsync<TResult>(Func<Task<TResult>> operation, CancellationToken ct)
        => ExecuteWithReadinessAsync(operation, ct);

    public async Task EnsureReady(CancellationToken ct = default)
    {
        var collectionName = AdapterNaming.GetOrCompute<TEntity, TKey>(_sp);
        var collectionKey = BuildCollectionKey();
        var schemaLock = GetSchemaLock(collectionKey);

        await schemaLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_healthyCache.TryGetValue(collectionKey, out var cached) && cached)
            {
                return;
            }

            var database = await _provider.GetDatabase(ct).ConfigureAwait(false);
            if (!await CollectionExists(database, collectionName, ct).ConfigureAwait(false))
            {
                await database.CreateCollectionAsync(collectionName, cancellationToken: ct).ConfigureAwait(false);
            }

            var collection = _collections.GetOrAdd(collectionName, n => database.GetCollection<TEntity>(n));

            await EnsureIndexes(collection, ct).ConfigureAwait(false);
            _healthyCache[collectionKey] = true;
        }
        catch (Exception ex)
        {
            _healthyCache[collectionKey] = false;
            _indexCache.TryRemove(BuildIndexKey(collectionName), out _);
            _logger?.LogWarning(ex, "Mongo schema health ensure failed for {Collection}", collectionName);
            throw;
        }
        finally
        {
            schemaLock.Release();
        }
    }

    private async Task<IMongoCollection<TEntity>> GetCollection(CancellationToken ct)
    {
        // Resolve the collection for THIS call's ambient partition into a local — never shared mutable state.
        // Concurrent flows under different partitions each resolve their own name and get their own handle,
        // so a write can never land in another partition's collection.
        var name = AdapterNaming.GetOrCompute<TEntity, TKey>(_sp);
        var key = BuildCollectionKey();
        if (!_healthyCache.TryGetValue(key, out var healthy) || !healthy)
        {
            await EnsureReady(ct).ConfigureAwait(false);
        }

        if (_collections.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var database = await _provider.GetDatabase(ct).ConfigureAwait(false);
        return _collections.GetOrAdd(name, n => database.GetCollection<TEntity>(n));
    }

    private static SemaphoreSlim GetSchemaLock(string key)
        => _schemaLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    private string BuildServerKey()
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return $"(inproc)|{options.Database ?? ""}";
        }
        try
        {
            var url = new MongoUrl(options.ConnectionString);
            var servers = url.Servers
                .Select(s => $"{s.Host}:{s.Port}")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            var hostSegment = servers.Length != 0 ? string.Join(',', servers) : url.Url;
            var database = !string.IsNullOrWhiteSpace(url.DatabaseName)
                ? url.DatabaseName
                : options.Database ?? "";
            return $"{hostSegment}|{database}";
        }
        catch
        {
            return $"{options.ConnectionString}|{options.Database}";
        }
    }

    private string BuildCollectionKey()
    {
        var storage = AdapterNaming.GetOrCompute<TEntity, TKey>(_sp);
        return $"{BuildServerKey()}|{storage}";
    }

    private string BuildIndexKey(string collectionName)
        => $"{BuildServerKey()}|{collectionName}|{typeof(TEntity).FullName}";

    // Instance (not static): index key field names must traverse the SAME element-name mapping as filters/sorts
    // (MapFieldName → camelCase + the _id carve-out). The string overload Ascending(string) takes a LITERAL field
    // name and does NOT apply the registered CamelCaseElementNameConvention, so building keys from the raw PascalCase
    // property name produced an index on fields the documents don't have (e.g. "VisibleAt" vs stored "visibleAt"),
    // leaving every [Index] uncovered → a blocking in-memory sort at scale. Map the name here (JOBS-0008 follow-up).
    private IReadOnlyList<CreateIndexModel<TEntity>> BuildIndexModels()
    {
        var models = new List<CreateIndexModel<TEntity>>();

        var indexSpecs = IndexMetadata.GetIndexes(typeof(TEntity));
        var keysBuilder = Builders<TEntity>.IndexKeys;

        foreach (var idx in indexSpecs)
        {
            if (idx.IsPrimaryKey || idx.Properties.Count == 0)
            {
                continue;
            }

            IndexKeysDefinition<TEntity>? keys = null;
            foreach (var property in idx.Properties)
            {
                var field = keysBuilder.Ascending(MapFieldName(property.Name));
                keys = keys is null ? field : keysBuilder.Combine(keys, field);
            }

            if (keys is null)
            {
                continue;
            }

            var name = !string.IsNullOrWhiteSpace(idx.Name)
                ? idx.Name!
                : $"ix_{string.Join("_", idx.Properties.Select(p => p.Name))}";

            var options = new CreateIndexOptions { Name = name, Unique = idx.Unique };
            // §20.4 TTL: a single-field [Index(Ttl=true)] timestamp index makes Mongo expire each row once its value is
            // in the past (expireAfterSeconds = 0). A null/absent value is never expired.
            if (idx.Ttl && idx.Properties.Count == 1)
                options.ExpireAfter = TimeSpan.Zero;
            models.Add(new CreateIndexModel<TEntity>(keys, options));
        }

        return models;
    }

    private async Task EnsureIndexes(IMongoCollection<TEntity> collection, CancellationToken ct)
    {
        var indexKey = BuildIndexKey(collection.CollectionNamespace.CollectionName);
        if (_indexCache.TryGetValue(indexKey, out var cached) && cached)
        {
            return;
        }

        var models = BuildIndexModels();
        if (models.Count == 0)
        {
            _indexCache[indexKey] = true;
            return;
        }

        try
        {
            await collection.Indexes.CreateManyAsync(models, cancellationToken: ct).ConfigureAwait(false);
            _indexCache[indexKey] = true;
        }
        catch (MongoCommandException ex) when (ex.CodeName is "IndexOptionsConflict" or "IndexKeySpecsConflict" or "IndexAlreadyExists")
        {
            _indexCache[indexKey] = true;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Mongo index ensure failed for collection {Collection}", collection.CollectionNamespace.CollectionName);
            _indexCache.TryRemove(indexKey, out _);
        }
    }

    private static async Task<bool> CollectionExists(IMongoDatabase database, string collectionName, CancellationToken ct)
    {
        var filter = new BsonDocument("name", collectionName);
        var options = new ListCollectionNamesOptions { Filter = filter };
        using var cursor = await database.ListCollectionNamesAsync(options, ct).ConfigureAwait(false);
        return await cursor.AnyAsync(ct).ConfigureAwait(false);
    }


    public Task<TEntity?> Get(TKey id, CancellationToken ct = default)
        => ExecuteWithReadinessAsync<TEntity?>(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.get");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollection(ct).ConfigureAwait(false);
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
            var result = await collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            return result;
        }, ct);

    public Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.get.many");
            activity?.SetTag("entity", typeof(TEntity).FullName);

            // Materialize IDs to preserve order and count
            var idList = ids as IReadOnlyList<TKey> ?? ids.ToList();
            if (idList.Count == 0)
            {
                return (IReadOnlyList<TEntity?>)[];
            }

            var collection = await GetCollection(ct).ConfigureAwait(false);
            var filter = Builders<TEntity>.Filter.In(x => x.Id, idList);
            var found = await collection.Find(filter).ToListAsync(ct).ConfigureAwait(false);

            // Build dictionary for O(1) lookup
            var entityMap = found.ToDictionary(e => e.Id);

            // Preserve order and include nulls for missing entities
            var results = new TEntity?[idList.Count];
            for (var i = 0; i < idList.Count; i++)
            {
                results[i] = entityMap.TryGetValue(idList[i], out var entity) ? entity : null;
            }

            return (IReadOnlyList<TEntity?>)results;
        }, ct);

    // ==================== Unified Query (IQueryRepository) ====================

    public Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.query");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollection(ct).ConfigureAwait(false);

            // Filter is guaranteed pushable per FilterSupport (the coordinator already split it).
            var filter = BuildFilter(query.Filter);

            // Sort pushdown: translate every spec; report exactly what we pushed.
            var (sortDef, sortHandled) = BuildSort(query.Sort);

            // Total count (cheap server-side) so the coordinator need not recount when there is no residual.
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
        }, ct);

    public Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.count");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollection(ct).ConfigureAwait(false);

            // Fast strategy on an unfiltered count uses Mongo's metadata estimate.
            if (query.CountStrategy == CountStrategy.Fast && query.Filter is null)
            {
                var estimate = await collection.EstimatedDocumentCountAsync(cancellationToken: ct).ConfigureAwait(false);
                return CountResult.Estimate((long)estimate);
            }

            var filter = BuildFilter(query.Filter);
            var count = await collection.CountDocumentsAsync(filter, cancellationToken: ct).ConfigureAwait(false);
            return CountResult.Exact((long)count);
        }, ct);

    /// <summary>Translate the (already-pushable) filter; null filter is match-all.</summary>
    private FilterDefinition<TEntity> BuildFilter(Filter? filter)
        => filter is null ? Builders<TEntity>.Filter.Empty : Translator.Translate(filter, typeof(TEntity));

    /// <summary>Translate sort specs into a Mongo sort definition, reporting which specs were pushed.</summary>
    private (SortDefinition<TEntity>? Def, IReadOnlySet<SortSpec> Handled) BuildSort(IReadOnlyList<SortSpec> specs)
    {
        if (specs.Count == 0) return (null, RepositoryQueryResult<TEntity>.NoSortHandled);

        var keys = Builders<TEntity>.Sort;
        SortDefinition<TEntity>? def = null;
        foreach (var spec in specs)
        {
            // Sort traverses the same field-name mapping as filters (camelCase + _id carve-out).
            var name = string.Join('.', spec.Path.Members.Select((m, i)
                => i == 0 ? MapFieldName(m.Name) : ToCamelCase(m.Name)));
            var part = spec.Desc ? keys.Descending(name) : keys.Ascending(name);
            def = def is null ? part : keys.Combine(def, part);
        }
        return (def, specs.ToFrozenSet());
    }

    public Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.upsert");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollection(ct).ConfigureAwait(false);
            var scope = ManagedFieldWriteScope.Current;
            if (scope is null || scope.Count == 0)
            {
                var filter = Builders<TEntity>.Filter.Eq(x => x.Id, model.Id);
                await collection.ReplaceOneAsync(filter, model, new ReplaceOptions { IsUpsert = true }, ct).ConfigureAwait(false);
            }
            else
            {
                await ManagedUpsertOneAsync(collection, model, scope, ct).ConfigureAwait(false);
            }
            return model;
        }, ct);

    // Managed-field conflict-aware upsert (DATA-0105 §3b — the write-verify half on Mongo). The entity is
    // serialized to a BsonDocument via the registered class map, the managed elements (e.g. __koan_tenant) are
    // injected (an invisible discriminator, not a POCO property — IgnoreExtraElements drops it on read), and the
    // replace runs through a BsonDocument view with a conflict-aware filter {_id, <managed conds>}. A foreign-owned
    // doc fails the filter → the upsert tries to INSERT the replacement with the same _id → E11000 duplicate key →
    // a rejected cross-scope write. Generic: reads the scope dict, never names tenant/classification.
    private static async Task ManagedUpsertOneAsync(
        IMongoCollection<TEntity> collection, TEntity model, IReadOnlyDictionary<string, object?> scope, CancellationToken ct)
    {
        var doc = model.ToBsonDocument();
        foreach (var kv in scope) doc[kv.Key] = ToBson(kv.Value);

        var docs = collection.Database.GetCollection<BsonDocument>(collection.CollectionNamespace.CollectionName);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]);
        foreach (var kv in scope)
            filter = Builders<BsonDocument>.Filter.And(filter, Builders<BsonDocument>.Filter.Eq(kv.Key, ToBson(kv.Value)));

        try
        {
            await docs.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, ct).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw CrossScopeWrite(collection.CollectionNamespace.CollectionName, doc["_id"].ToString() ?? "");
        }
    }

    private static BsonValue ToBson(object? value) => value is null ? BsonNull.Value : BsonValue.Create(value);

    private static InvalidOperationException CrossScopeWrite(string collection, string id)
        => new($"Rejected a cross-scope write to '{collection}' id '{id}': the document is owned by a different managed " +
               "scope (e.g. tenant/classification). A managed-field-scoped entity cannot overwrite another scope's document.");

    public Task<bool> Delete(TKey id, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.delete");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollection(ct).ConfigureAwait(false);
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
            var result = await collection.DeleteOneAsync(filter, ct).ConfigureAwait(false);
            var deleted = result.DeletedCount > 0;
            //_logger?.LogDebug("Mongo delete {Entity} id={Id} deleted={Deleted}", typeof(TEntity).Name, id, deleted);
            return deleted;
        }, ct);

    public Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.bulk.upsert");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollection(ct).ConfigureAwait(false);
            var modelList = models as IReadOnlyCollection<TEntity> ?? models.ToList();
            if (modelList.Count == 0)
            {
                return 0;
            }

            var scope = ManagedFieldWriteScope.Current;
            if (scope is not null && scope.Count > 0)
            {
                // Under a managed scope, replace per document (the conflict-aware path); the bulk path stays the
                // byte-identical fast path when nothing is in scope.
                var n = 0;
                foreach (var model in modelList)
                {
                    ct.ThrowIfCancellationRequested();
                    await ManagedUpsertOneAsync(collection, model, scope, ct).ConfigureAwait(false);
                    n++;
                }
                return n;
            }

            var writes = modelList
                .Select(model => new ReplaceOneModel<TEntity>(Builders<TEntity>.Filter.Eq(x => x.Id, model.Id), model)
                {
                    IsUpsert = true
                })
                .ToArray();

            var result = await collection.BulkWriteAsync(writes, cancellationToken: ct).ConfigureAwait(false);
            var count = (int)(result.ModifiedCount + result.Upserts.Count);
            //_logger?.LogDebug("Mongo bulk upsert {Entity} count={Count}", typeof(TEntity).Name, count);
            return count;
        }, ct);

    public Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.bulk.delete");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollection(ct).ConfigureAwait(false);
            var keys = ids as ICollection<TKey> ?? ids.ToArray();
            if (keys.Count == 0)
            {
                return 0;
            }

            var filter = Builders<TEntity>.Filter.In(x => x.Id, keys);
            var result = await collection.DeleteManyAsync(filter, ct).ConfigureAwait(false);
            var count = (int)result.DeletedCount;
            //_logger?.LogDebug("Mongo bulk delete {Entity} count={Count}", typeof(TEntity).Name, count);
            return count;
        }, ct);

    public Task<int> DeleteAll(CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var collection = await GetCollection(ct).ConfigureAwait(false);
            var result = await collection.DeleteManyAsync(Builders<TEntity>.Filter.Empty, ct).ConfigureAwait(false);
            return (int)result.DeletedCount;
        }, ct);

    public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var collection = await GetCollection(ct).ConfigureAwait(false);

            // Resolve Optimized strategy based on provider capabilities
            var effectiveStrategy = strategy == RemoveStrategy.Optimized
                ? RemoveStrategy.Fast // this adapter declares write.fastRemove
                : strategy;

            if (effectiveStrategy == RemoveStrategy.Fast)
            {
                // Fast path: drop collection and recreate (loses indexes briefly). Operate on the collection
                // resolved for THIS call's partition — never a shared field.
                var database = GetDatabase(collection);
                var name = collection.CollectionNamespace.CollectionName;
                var estimatedCount = await collection.EstimatedDocumentCountAsync(cancellationToken: ct).ConfigureAwait(false);

                await database.DropCollectionAsync(name, ct).ConfigureAwait(false);
                await database.CreateCollectionAsync(name, cancellationToken: ct).ConfigureAwait(false);

                // Recreate collection reference and indexes
                var recreated = database.GetCollection<TEntity>(name);
                _collections[name] = recreated;
                await EnsureIndexes(recreated, ct).ConfigureAwait(false);

                return estimatedCount;
            }

            // Safe path: deleteMany (fires hooks if registered)
            var result = await collection.DeleteManyAsync(Builders<TEntity>.Filter.Empty, ct).ConfigureAwait(false);
            return result.DeletedCount;
        }, ct);

    public Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.instruction");
            activity?.SetTag("entity", typeof(TEntity).FullName);

            switch (instruction.Name)
            {
                case DataInstructions.EnsureCreated:
                    {
                        var collection = await GetCollection(ct).ConfigureAwait(false);
                        var database = GetDatabase(collection);
                        var name = collection.CollectionNamespace.CollectionName;

                        try
                        {
                            var existing = await database.ListCollectionNamesAsync(cancellationToken: ct).ConfigureAwait(false);
                            var names = await existing.ToListAsync(ct).ConfigureAwait(false);
                            if (!names.Contains(name, StringComparer.Ordinal))
                            {
                                await database.CreateCollectionAsync(name, cancellationToken: ct).ConfigureAwait(false);
                                _logger?.LogDebug("Mongo ensureCreated created collection {Name}", name);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Mongo ensureCreated encountered an error for collection {Name}", name);
                        }

                        object ok = true;
                        return (TResult)ok;
                    }
                case DataInstructions.Clear:
                    {
                        var deleted = await DeleteAll(ct).ConfigureAwait(false);
                        object result = deleted;
                        return (TResult)result;
                    }
                default:
                    throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by Mongo adapter for {typeof(TEntity).Name}.");
            }
        }, ct);

    public IBatchSet<TEntity, TKey> CreateBatch() => new MongoBatch(this);

    private static IMongoDatabase GetDatabase(IMongoCollection<TEntity> collection)
    {
        try
        {
            return collection.Database;
        }
        catch
        {
            var prop = typeof(IMongoCollection<TEntity>).GetProperty("Database");
            if (prop?.GetValue(collection) is IMongoDatabase database)
            {
                return database;
            }

            throw new InvalidOperationException("Unable to obtain Mongo database from collection.");
        }
    }

    private string MapFieldName(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return propertyName;
        }

        if (!string.IsNullOrWhiteSpace(_optimizationInfo.IdPropertyName) &&
            string.Equals(propertyName, _optimizationInfo.IdPropertyName, StringComparison.Ordinal))
        {
            return "_id";
        }

        if (string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            return "_id";
        }

        return ToCamelCase(propertyName);
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (!char.IsUpper(value[0]))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToLowerInvariant();
        }

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    private sealed class MongoBatch : IBatchSet<TEntity, TKey>
    {
        private readonly MongoRepository<TEntity, TKey> _repo;
        private readonly List<WriteModel<TEntity>> _operations = new();
        private readonly List<(TKey Id, Action<TEntity> Mutate)> _mutations = new();

        public MongoBatch(MongoRepository<TEntity, TKey> repo) => _repo = repo;

        public IBatchSet<TEntity, TKey> Add(TEntity entity)
        {
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id);
            _operations.Add(new ReplaceOneModel<TEntity>(filter, entity) { IsUpsert = true });
            return this;
        }

        public IBatchSet<TEntity, TKey> Update(TEntity entity) => Add(entity);

        public IBatchSet<TEntity, TKey> Delete(TKey id)
        {
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
            _operations.Add(new DeleteOneModel<TEntity>(filter));
            return this;
        }

        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate)
        {
            _mutations.Add((id, mutate));
            return this;
        }

        public IBatchSet<TEntity, TKey> Clear()
        {
            _operations.Clear();
            _mutations.Clear();
            return this;
        }

        public async Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            if (_mutations.Count != 0)
            {
                foreach (var (id, mutate) in _mutations)
                {
                    var current = await _repo.Get(id, ct).ConfigureAwait(false);
                    if (current is not null)
                    {
                        mutate(current);
                        var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
                        _operations.Add(new ReplaceOneModel<TEntity>(filter, current) { IsUpsert = true });
                    }
                }
            }

            if (_operations.Count == 0)
            {
                return new BatchResult(0, 0, 0);
            }

            return await _repo.ExecuteWithinReadinessAsync(async () =>
            {
                var collection = await _repo.GetCollection(ct).ConfigureAwait(false);

                if (options?.RequireAtomic == true)
                {
                    var database = GetDatabase(collection);
                    var client = database.Client;
                    IClientSessionHandle? session = null;

                    try
                    {
                        session = await client.StartSessionAsync(cancellationToken: ct).ConfigureAwait(false);
                        try
                        {
                            session.StartTransaction();
                        }
                        catch (MongoClientException ex) when (ex.Message.Contains("Transactions are not supported", StringComparison.OrdinalIgnoreCase))
                        {
                            session.Dispose();
                            throw new NotSupportedException("MongoDB deployment does not support transactions; cannot honor RequireAtomic=true.", ex);
                        }

                        var result = await collection.BulkWriteAsync(session, _operations, cancellationToken: ct).ConfigureAwait(false);
                        await session.CommitTransactionAsync(ct).ConfigureAwait(false);
                        return new BatchResult(result.Upserts.Count, (int)result.ModifiedCount, (int)result.DeletedCount);
                    }
                    catch
                    {
                        if (session is not null)
                        {
                            try
                            {
                                await session.AbortTransactionAsync(ct).ConfigureAwait(false);
                            }
                            catch
                            {
                                // swallow abort failures
                            }

                            session.Dispose();
                        }

                        throw;
                    }
                }
                else
                {
                    var result = await collection.BulkWriteAsync(_operations, cancellationToken: ct).ConfigureAwait(false);
                    return new BatchResult(result.Upserts.Count, (int)result.ModifiedCount, (int)result.DeletedCount);
                }
            }, ct).ConfigureAwait(false);
        }
    }
}

