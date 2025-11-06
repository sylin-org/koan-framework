using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Adapters;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.Configuration;
using Koan.Data.Core.Extensions;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo;

internal sealed class MongoRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IDataRepositoryWithOptions<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>,
    IInstructionExecutor<TEntity>,
    IAdapterReadiness,
    IAdapterReadinessConfiguration,
    ISchemaHealthContributor<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly MongoClientProvider _provider;
    private readonly IOptionsMonitor<MongoOptions> _options;
    private readonly IServiceProvider _sp;
    private readonly ILogger? _logger;
    private readonly StorageOptimizationInfo _optimizationInfo;
    private IMongoCollection<TEntity>? _collection;
    private string _collectionName;

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
        _collectionName = StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
    }

    public QueryCapabilities Capabilities => QueryCapabilities.Linq;
    public WriteCapabilities Writes => WriteCapabilities.BulkUpsert | WriteCapabilities.BulkDelete | WriteCapabilities.AtomicBatch | WriteCapabilities.FastRemove;
    public StorageOptimizationInfo OptimizationInfo => _optimizationInfo;

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

    public Task WaitForReadinessAsync(TimeSpan? timeout = null, CancellationToken ct = default)
        => _provider.WaitForReadinessAsync(timeout ?? Timeout, ct);

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

    private Task ExecuteWithReadinessAsync(Func<Task> operation, CancellationToken ct)
        => this.WithReadinessAsync(operation, ct);

    internal Task<TResult> ExecuteWithinReadinessAsync<TResult>(Func<Task<TResult>> operation, CancellationToken ct)
        => ExecuteWithReadinessAsync(operation, ct);

    public async Task EnsureHealthyAsync(CancellationToken ct)
    {
        var collectionName = StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        var collectionKey = BuildCollectionKey();
        var schemaLock = GetSchemaLock(collectionKey);

        await schemaLock.WaitAsync(ct);
        try
        {
            if (_healthyCache.TryGetValue(collectionKey, out var cached) && cached)
            {
                return;
            }

            var database = await _provider.GetDatabaseAsync(ct);
            if (!await CollectionExistsAsync(database, collectionName, ct))
            {
                await database.CreateCollectionAsync(collectionName, cancellationToken: ct);
            }

            _collection = database.GetCollection<TEntity>(collectionName);
            _collectionName = collectionName;

            await EnsureIndexesAsync(_collection, ct);
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

    public void InvalidateHealth()
    {
        var collectionName = _collectionName;
        var collectionKey = BuildCollectionKey();
        _healthyCache.TryRemove(collectionKey, out _);

        if (!string.IsNullOrWhiteSpace(collectionName))
        {
            _indexCache.TryRemove(BuildIndexKey(collectionName), out _);
        }
    }

    private async Task<IMongoCollection<TEntity>> GetCollectionAsync(CancellationToken ct)
    {
        var key = BuildCollectionKey();
        if (!_healthyCache.TryGetValue(key, out var healthy) || !healthy)
        {
            await EnsureHealthyAsync(ct);
        }

        return await GetCollectionCoreAsync(ct);
    }

    private async Task<IMongoCollection<TEntity>> GetCollectionCoreAsync(CancellationToken ct)
    {
        var desired = StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        if (_collection is not null && string.Equals(desired, _collectionName, StringComparison.Ordinal))
        {
            return _collection;
        }

        var database = await _provider.GetDatabaseAsync(ct);
        _collection = database.GetCollection<TEntity>(desired);
        _collectionName = desired;
        return _collection;
    }

    private static SemaphoreSlim GetSchemaLock(string key)
        => _schemaLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    private string BuildServerKey()
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return $"(inproc)|{options.Database ?? string.Empty}";
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
                : options.Database ?? string.Empty;
            return $"{hostSegment}|{database}";
        }
        catch
        {
            return $"{options.ConnectionString}|{options.Database}";
        }
    }

    private string BuildCollectionKey()
    {
        var storage = StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        return $"{BuildServerKey()}|{storage}";
    }

    private string BuildIndexKey(string collectionName)
        => $"{BuildServerKey()}|{collectionName}|{typeof(TEntity).FullName}";

    private static IReadOnlyList<CreateIndexModel<TEntity>> BuildIndexModels()
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
                var field = keysBuilder.Ascending(property.Name);
                keys = keys is null ? field : keysBuilder.Combine(keys, field);
            }

            if (keys is null)
            {
                continue;
            }

            var name = !string.IsNullOrWhiteSpace(idx.Name)
                ? idx.Name!
                : $"ix_{string.Join("_", idx.Properties.Select(p => p.Name))}";

            models.Add(new CreateIndexModel<TEntity>(keys, new CreateIndexOptions
            {
                Name = name,
                Unique = idx.Unique
            }));
        }

        return models;
    }

    private async Task EnsureIndexesAsync(IMongoCollection<TEntity> collection, CancellationToken ct)
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
            await collection.Indexes.CreateManyAsync(models, cancellationToken: ct);
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

    private static async Task<bool> CollectionExistsAsync(IMongoDatabase database, string collectionName, CancellationToken ct)
    {
        var filter = new BsonDocument("name", collectionName);
        var options = new ListCollectionNamesOptions { Filter = filter };
        using var cursor = await database.ListCollectionNamesAsync(options, ct);
        return await cursor.AnyAsync(ct);
    }


    public Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
        => ExecuteWithReadinessAsync<TEntity?>(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.get");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollectionAsync(ct);
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
            var result = await collection.Find(filter).FirstOrDefaultAsync(ct);
            return result;
        }, ct);

    public Task<IReadOnlyList<TEntity?>> GetManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.get.many");
            activity?.SetTag("entity", typeof(TEntity).FullName);

            // Materialize IDs to preserve order and count
            var idList = ids as IReadOnlyList<TKey> ?? ids.ToList();
            if (idList.Count == 0)
            {
                return (IReadOnlyList<TEntity?>)Array.Empty<TEntity?>();
            }

            var collection = await GetCollectionAsync(ct);
            var filter = Builders<TEntity>.Filter.In(x => x.Id, idList);
            var found = await collection.Find(filter).ToListAsync(ct);

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

    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.query.all");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollectionAsync(ct);

            IFindFluent<TEntity, TEntity> cursor = query is Expression<Func<TEntity, bool>> predicate
                ? FindWithPredicate(collection, predicate)
                : collection.Find(Builders<TEntity>.Filter.Empty);

            var results = await cursor.ToListAsync(ct);
            return (IReadOnlyList<TEntity>)results;
        }, ct);

    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var (defaultPageSize, maxPageSize) = _options.CurrentValue.GetPagingGuardrails();
            var collection = await GetCollectionAsync(ct);

            IFindFluent<TEntity, TEntity> cursor = query is Expression<Func<TEntity, bool>> predicate
                ? FindWithPredicate(collection, predicate)
                : collection.Find(Builders<TEntity>.Filter.Empty);

            cursor = cursor.ApplyPaging(options, defaultPageSize, maxPageSize,
                (c, skip, take) => c.Skip(skip).Limit(take));

            var results = await cursor.ToListAsync(ct);
            return (IReadOnlyList<TEntity>)results;
        }, ct);

    public Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => QueryAsync(predicate, null, ct);

    public Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.query.linq");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var (defaultPageSize, maxPageSize) = _options.CurrentValue.GetPagingGuardrails();
            var collection = await GetCollectionAsync(ct);

            var cursor = FindWithPredicate(collection, predicate)
                .ApplyPaging(options, defaultPageSize, maxPageSize,
                    (c, skip, take) => c.Skip(skip).Limit(take));

            var results = await cursor.ToListAsync(ct);
            return (IReadOnlyList<TEntity>)results;
        }, ct);

    public Task<CountResult> CountAsync(CountRequest<TEntity> request, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.count");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollectionAsync(ct);

            if (request.Strategy == CountStrategy.Fast && request.Predicate is null && request.RawQuery is null && request.ProviderQuery is null)
            {
                var estimate = await collection.EstimatedDocumentCountAsync(cancellationToken: ct);
                return CountResult.Estimate((long)estimate);
            }

            if (request.Predicate is not null)
            {
                if (TryBuildGuidFilter(request.Predicate, out var optimizedFilter))
                {
                    var optimizedCount = await collection.CountDocumentsAsync(optimizedFilter, cancellationToken: ct);
                    return CountResult.Exact((long)optimizedCount);
                }

                var count = await collection.CountDocumentsAsync(request.Predicate, cancellationToken: ct);
                return CountResult.Exact((long)count);
            }

            if (request.ProviderQuery is FilterDefinition<TEntity> filter)
            {
                var count = await collection.CountDocumentsAsync(filter, cancellationToken: ct);
                return CountResult.Exact((long)count);
            }

            if (request.RawQuery is not null)
            {
                throw new NotSupportedException("Mongo adapter does not support string-based count queries. Use LINQ or provider filters.");
            }

            var total = await collection.CountDocumentsAsync(Builders<TEntity>.Filter.Empty, cancellationToken: ct);
            return CountResult.Exact((long)total);
        }, ct);

    public Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.upsert");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollectionAsync(ct);
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, model.Id);
            await collection.ReplaceOneAsync(filter, model, new ReplaceOptions { IsUpsert = true }, ct);
            //_logger?.LogDebug("Mongo upsert {Entity} id={Id}", typeof(TEntity).Name, model.Id);
            return model;
        }, ct);

    public Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.delete");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollectionAsync(ct);
            var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
            var result = await collection.DeleteOneAsync(filter, ct);
            var deleted = result.DeletedCount > 0;
            //_logger?.LogDebug("Mongo delete {Entity} id={Id} deleted={Deleted}", typeof(TEntity).Name, id, deleted);
            return deleted;
        }, ct);

    public Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.bulk.upsert");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollectionAsync(ct);
            var writes = models
                .Select(model => new ReplaceOneModel<TEntity>(Builders<TEntity>.Filter.Eq(x => x.Id, model.Id), model)
                {
                    IsUpsert = true
                })
                .ToArray();

            if (writes.Length == 0)
            {
                return 0;
            }

            var result = await collection.BulkWriteAsync(writes, cancellationToken: ct);
            var count = (int)(result.ModifiedCount + result.Upserts.Count);
            //_logger?.LogDebug("Mongo bulk upsert {Entity} count={Count}", typeof(TEntity).Name, count);
            return count;
        }, ct);

    public Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var activity = MongoTelemetry.Activity.StartActivity("mongo.bulk.delete");
            activity?.SetTag("entity", typeof(TEntity).FullName);
            var collection = await GetCollectionAsync(ct);
            var keys = ids as ICollection<TKey> ?? ids.ToArray();
            if (keys.Count == 0)
            {
                return 0;
            }

            var filter = Builders<TEntity>.Filter.In(x => x.Id, keys);
            var result = await collection.DeleteManyAsync(filter, ct);
            var count = (int)result.DeletedCount;
            //_logger?.LogDebug("Mongo bulk delete {Entity} count={Count}", typeof(TEntity).Name, count);
            return count;
        }, ct);

    public Task<int> DeleteAllAsync(CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var collection = await GetCollectionAsync(ct);
            var result = await collection.DeleteManyAsync(Builders<TEntity>.Filter.Empty, ct);
            return (int)result.DeletedCount;
        }, ct);

    public Task<long> RemoveAllAsync(RemoveStrategy strategy, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var collection = await GetCollectionAsync(ct);

            // Resolve Optimized strategy based on provider capabilities
            var effectiveStrategy = strategy == RemoveStrategy.Optimized
                ? (Writes.HasFlag(WriteCapabilities.FastRemove) ? RemoveStrategy.Fast : RemoveStrategy.Safe)
                : strategy;

            if (effectiveStrategy == RemoveStrategy.Fast)
            {
                // Fast path: drop collection and recreate (loses indexes briefly)
                var database = GetDatabase(collection);
                var estimatedCount = await collection.EstimatedDocumentCountAsync(cancellationToken: ct);

                await database.DropCollectionAsync(_collectionName, ct);
                await database.CreateCollectionAsync(_collectionName, cancellationToken: ct);

                // Recreate collection reference and indexes
                _collection = database.GetCollection<TEntity>(_collectionName);
                await EnsureIndexesAsync(_collection, ct);

                return estimatedCount;
            }

            // Safe path: deleteMany (fires hooks if registered)
            var result = await collection.DeleteManyAsync(Builders<TEntity>.Filter.Empty, ct);
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
                        var collection = await GetCollectionAsync(ct);
                        var database = GetDatabase(collection);

                        try
                        {
                            var existing = await database.ListCollectionNamesAsync(cancellationToken: ct);
                            var names = await existing.ToListAsync(ct);
                            if (!names.Contains(_collectionName, StringComparer.Ordinal))
                            {
                                await database.CreateCollectionAsync(_collectionName, cancellationToken: ct);
                                _logger?.LogDebug("Mongo ensureCreated created collection {Name}", _collectionName);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Mongo ensureCreated encountered an error for collection {Name}", _collectionName);
                        }

                        object ok = true;
                        return (TResult)ok;
                    }
                case DataInstructions.Clear:
                    {
                        var deleted = await DeleteAllAsync(ct);
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

    private IFindFluent<TEntity, TEntity> FindWithPredicate(IMongoCollection<TEntity> collection, Expression<Func<TEntity, bool>> predicate)
    {
        if (TryBuildGuidFilter(predicate, out var optimizedFilter))
        {
            return collection.Find(optimizedFilter);
        }

        return collection.Find(predicate);
    }

    private bool TryBuildGuidFilter(Expression<Func<TEntity, bool>> predicate, out FilterDefinition<TEntity> filter)
    {
        filter = Builders<TEntity>.Filter.Empty;
        if (predicate is null || predicate.Parameters.Count == 0)
        {
            return false;
        }

        return TryBuildGuidFilter(predicate.Parameters[0], predicate.Body, out filter);
    }

    private bool TryBuildGuidFilter(ParameterExpression parameter, Expression expression, out FilterDefinition<TEntity> filter)
    {
        filter = Builders<TEntity>.Filter.Empty;
        expression = StripConvert(expression);

        if (expression is BinaryExpression binary)
        {
            switch (binary.NodeType)
            {
                case ExpressionType.Equal:
                    if (TryExtractComparison(parameter, binary.Left, binary.Right, out var fieldName, out var value) ||
                        TryExtractComparison(parameter, binary.Right, binary.Left, out fieldName, out value))
                    {
                        if (value is string str && Guid.TryParse(str, out var guid))
                        {
                            var elementName = MapFieldName(fieldName);
                            filter = Builders<TEntity>.Filter.Eq(elementName, new BsonBinaryData(guid, GuidRepresentation.Standard));
                            return true;
                        }
                    }
                    break;
                case ExpressionType.AndAlso:
                    if (TryBuildGuidFilter(parameter, binary.Left, out var left) && TryBuildGuidFilter(parameter, binary.Right, out var right))
                    {
                        filter = Builders<TEntity>.Filter.And(left, right);
                        return true;
                    }
                    break;
                case ExpressionType.OrElse:
                    if (TryBuildGuidFilter(parameter, binary.Left, out var leftOr) && TryBuildGuidFilter(parameter, binary.Right, out var rightOr))
                    {
                        filter = Builders<TEntity>.Filter.Or(leftOr, rightOr);
                        return true;
                    }
                    break;
            }
        }

        return false;
    }

    private bool TryExtractComparison(ParameterExpression parameter, Expression fieldExpression, Expression valueExpression, out string fieldName, out object? value)
    {
        fieldName = string.Empty;
        value = null;

        var candidate = StripConvert(fieldExpression) as MemberExpression;
        if (candidate is null)
        {
            return false;
        }

        if (candidate.Expression is null)
        {
            return false;
        }

        var owner = StripConvert(candidate.Expression);
        if (owner != parameter)
        {
            return false;
        }

        fieldName = candidate.Member.Name;
        return TryEvaluateExpression(valueExpression, out value);
    }

    private static Expression StripConvert(Expression expression)
    {
        while (expression.NodeType is ExpressionType.Convert or ExpressionType.ConvertChecked)
        {
            expression = ((UnaryExpression)expression).Operand;
        }

        return expression;
    }

    private bool TryEvaluateExpression(Expression expression, out object? value)
    {
        expression = StripConvert(expression);

        try
        {
            var lambda = Expression.Lambda(expression);
            value = lambda.Compile().DynamicInvoke();
            return true;
        }
        catch
        {
            value = null;
            return false;
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

        public async Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default)
        {
            if (_mutations.Count != 0)
            {
                foreach (var (id, mutate) in _mutations)
                {
                    var current = await _repo.GetAsync(id, ct);
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
                var collection = await _repo.GetCollectionAsync(ct);

                if (options?.RequireAtomic == true)
                {
                    var database = GetDatabase(collection);
                    var client = database.Client;
                    IClientSessionHandle? session = null;

                    try
                    {
                        session = await client.StartSessionAsync(cancellationToken: ct);
                        try
                        {
                            session.StartTransaction();
                        }
                        catch (MongoClientException ex) when (ex.Message.Contains("Transactions are not supported", StringComparison.OrdinalIgnoreCase))
                        {
                            session.Dispose();
                            throw new NotSupportedException("MongoDB deployment does not support transactions; cannot honor RequireAtomic=true.", ex);
                        }

                        var result = await collection.BulkWriteAsync(session, _operations, cancellationToken: ct);
                        await session.CommitTransactionAsync(ct);
                        return new BatchResult(result.Upserts.Count, (int)result.ModifiedCount, (int)result.DeletedCount);
                    }
                    catch
                    {
                        if (session is not null)
                        {
                            try
                            {
                                await session.AbortTransactionAsync(ct);
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
                    var result = await collection.BulkWriteAsync(_operations, cancellationToken: ct);
                    return new BatchResult(result.Upserts.Count, (int)result.ModifiedCount, (int)result.DeletedCount);
                }
            }, ct);
        }
    }
}

