using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Couchbase;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Couchbase.Query;
using Couchbase.Transactions;
using Couchbase.Core.Exceptions.KeyValue;
using Couchbase.Transactions.Error;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Couchbase.Infrastructure;
using Koan.Data.Core.Configuration;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KvDurabilityLevel = Couchbase.KeyValue.DurabilityLevel;
using Koan.Core.Adapters;

namespace Koan.Data.Connector.Couchbase;

internal sealed class CouchbaseRepository<TEntity, TKey> :
    IDataRepositoryWithOptions<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
    IOptimizedDataRepository<TEntity, TKey>,
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
    private readonly CouchbaseClusterProvider _provider;
    private readonly IServiceProvider _sp;
    private readonly ILogger? _logger;
    private readonly CouchbaseOptions _options;
    private readonly StorageOptimizationInfo _optimizationInfo;
    private readonly KvDurabilityLevel? _kvDurability;
    private string _collectionName;

    public AdapterReadinessState ReadinessState => _provider.ReadinessState;

    public bool IsReady => _provider.IsReady;

    public TimeSpan ReadinessTimeout => _provider.ReadinessTimeout;

    public event EventHandler<ReadinessStateChangedEventArgs>? ReadinessStateChanged
    {
        add => _provider.ReadinessStateChanged += value;
        remove => _provider.ReadinessStateChanged -= value;
    }

    public ReadinessStateManager StateManager => _provider.StateManager;

    public CouchbaseRepository(CouchbaseClusterProvider provider, IStorageNameResolver resolver, IServiceProvider sp, IOptions<CouchbaseOptions> options)
    {
        _provider = provider;
        _sp = sp;
        _logger = sp.GetService<ILogger<CouchbaseRepository<TEntity, TKey>>>();
        _options = options.Value;
        _optimizationInfo = sp.GetStorageOptimization<TEntity, TKey>();
        _collectionName = StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        ArgumentNullException.ThrowIfNull(resolver);

        if (!string.IsNullOrWhiteSpace(_options.DurabilityLevel))
        {
            if (Enum.TryParse<KvDurabilityLevel>(_options.DurabilityLevel, true, out var kv))
            {
                _kvDurability = kv;
            }
        }
    }

    public QueryCapabilities Capabilities => QueryCapabilities.String | QueryCapabilities.Linq;
    public WriteCapabilities Writes => WriteCapabilities.BulkUpsert | WriteCapabilities.BulkDelete | WriteCapabilities.AtomicBatch;
    public StorageOptimizationInfo OptimizationInfo => _optimizationInfo;

    public Task<bool> IsReadyAsync(CancellationToken ct = default) => _provider.IsReadyAsync(ct);

    public Task WaitForReadiness(TimeSpan? timeout = null, CancellationToken ct = default)
        => _provider.WaitForReadiness(timeout ?? Timeout, ct);

    public ReadinessPolicy Policy => _options.Readiness.Policy;

    public TimeSpan Timeout
    {
        get
        {
            var timeout = _options.Readiness.Timeout;
            return timeout > TimeSpan.Zero ? timeout : _provider.ReadinessTimeout;
        }
    }

    public bool EnableReadinessGating => _options.Readiness.EnableReadinessGating;

    private Task<TResult> ExecuteWithReadinessAsync<TResult>(Func<Task<TResult>> operation, CancellationToken ct)
        => this.WithReadinessAsync<TResult, TEntity>(operation, ct);

    private Task ExecuteWithReadiness(Func<Task> operation, CancellationToken ct)
        => this.WithReadiness(operation, ct);

    private async ValueTask<CouchbaseCollectionContext> ResolveCollection(CancellationToken ct)
    {
        var desired = StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        if (!string.Equals(_collectionName, desired, StringComparison.Ordinal))
        {
            _collectionName = desired;
        }

        return await _provider.GetCollectionContext(_collectionName, ct);
    }

    public Task<TEntity?> Get(TKey id, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.get");
            act?.SetTag("entity", typeof(TEntity).FullName);
            var ctx = await ResolveCollection(ct);
            try
            {
                var result = await ctx.Collection.GetAsync(GetKey(id), new GetOptions().CancellationToken(ct));
                return result.ContentAs<TEntity>();
            }
            catch (DocumentNotFoundException)
            {
                return null;
            }
            catch (global::Couchbase.Core.Exceptions.UnambiguousTimeoutException ex) when (IsCollectionNotFound(ex))
            {
                await EnsureCollection(ctx, ct);

                var result = await ctx.Collection.GetAsync(GetKey(id), new GetOptions().CancellationToken(ct));
                return result.ContentAs<TEntity>();
            }
        }, ct);

    public Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.get.many");
            act?.SetTag("entity", typeof(TEntity).FullName);

            var idList = ids as IReadOnlyList<TKey> ?? ids.ToList();
            if (idList.Count == 0)
            {
                return (IReadOnlyList<TEntity?>)[];
            }

            var ctx = await ResolveCollection(ct);

            // Build list of keys for batch get
            var keys = idList.Select(id => GetKey(id)).ToList();

            try
            {
                // Couchbase supports batch get via GetAsync for multiple keys
                var tasks = keys.Select(key => ctx.Collection.GetAsync(key, new GetOptions().CancellationToken(ct)));
                var allResults = await Task.WhenAll(tasks);

                // Build dictionary for O(1) lookup
                var entityMap = new Dictionary<TKey, TEntity>();
                for (var i = 0; i < allResults.Length; i++)
                {
                    if (allResults[i] != null)
                    {
                        entityMap[idList[i]] = allResults[i].ContentAs<TEntity>();
                    }
                }

                // Preserve order and include nulls
                var results = new TEntity?[idList.Count];
                for (var i = 0; i < idList.Count; i++)
                {
                    results[i] = entityMap.TryGetValue(idList[i], out var entity) ? entity : null;
                }

                return (IReadOnlyList<TEntity?>)results;
            }
            catch (DocumentNotFoundException)
            {
                // Some documents don't exist - return nulls for missing
                var results = new TEntity?[idList.Count];
                for (var i = 0; i < idList.Count; i++)
                {
                    try
                    {
                        var result = await ctx.Collection.GetAsync(keys[i], new GetOptions().CancellationToken(ct));
                        results[i] = result.ContentAs<TEntity>();
                    }
                    catch (DocumentNotFoundException)
                    {
                        results[i] = null;
                    }
                }
                return (IReadOnlyList<TEntity?>)results;
            }
        }, ct);

    public Task<IReadOnlyList<TEntity>> Query(object? query, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(() => QueryInternal(query, null, ct), ct);

    public Task<RepositoryQueryResult<TEntity>> Query(object? query, DataQueryOptions? options, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            var items = await QueryInternal(query, options, ct);
            return RepositoryQueryResult<TEntity>.PaginatedOnly(items);
        }, ct);

    public Task<IReadOnlyList<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var result = await QueryPredicate(predicate, options: null, ct);
            return result;
        }, ct);

    public Task<RepositoryQueryResult<TEntity>> Query(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            var items = await QueryPredicate(predicate, options, ct);
            return RepositoryQueryResult<TEntity>.PaginatedOnly(items);
        }, ct);

    private async Task<IReadOnlyList<TEntity>> QueryPredicate(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.query.linq");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var ctx = await ResolveCollection(ct);
        if (!CouchbaseLinqQueryTranslator.TryTranslate<TEntity, TKey>(predicate, _optimizationInfo, out var translation))
        {
            throw new NotSupportedException($"Unable to translate expression '{predicate}' to N1QL for Couchbase.");
        }

        var statement = $"SELECT RAW doc FROM `{ctx.BucketName}`.`{ctx.ScopeName}`.`{ctx.CollectionName}` AS doc WHERE {translation.WhereClause}";
        var definition = new CouchbaseQueryDefinition(statement)
        {
            Parameters = translation.Parameters.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value)
        };

        return await ExecuteQuery(ctx, statement, definition, options, ct);
    }

    private async Task<IReadOnlyList<TEntity>> QueryInternal(object? query, DataQueryOptions? options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ctx = await ResolveCollection(ct);
        var definition = query switch
        {
            CouchbaseQueryDefinition def => def,
            string queryStatement when !string.IsNullOrWhiteSpace(queryStatement) => new CouchbaseQueryDefinition(queryStatement),
            _ => null
        };

        var statement = definition?.Statement ??
            $"SELECT RAW doc FROM `{ctx.BucketName}`.`{ctx.ScopeName}`.`{ctx.CollectionName}` AS doc";

        return await ExecuteQuery(ctx, statement, definition, options, ct);
    }

    public Task<CountResult> Count(CountRequest<TEntity> request, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.count");
            act?.SetTag("entity", typeof(TEntity).FullName);
            var ctx = await ResolveCollection(ct);

            CouchbaseQueryDefinition? definition = null;
            string statement;
            // The fully-qualified keyspace path the predicate/no-filter variants need to
            // SELECT from. The original code shipped with a literal `FROM ..` template that
            // was never filled in, producing "syntax error - at ." on every COUNT request.
            var fullKeyspace = $"`{ctx.BucketName}`.`{ctx.ScopeName}`.`{ctx.CollectionName}`";

            if (request.Predicate is not null)
            {
                if (!CouchbaseLinqQueryTranslator.TryTranslate<TEntity, TKey>(request.Predicate, _optimizationInfo, out var translation))
                {
                    throw new NotSupportedException($"Unable to translate expression '{request.Predicate}' to N1QL for Couchbase.");
                }

                statement = $"SELECT RAW COUNT(*) FROM {fullKeyspace} AS doc WHERE {translation.WhereClause}";
                definition = new CouchbaseQueryDefinition(statement)
                {
                    Parameters = translation.Parameters.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value)
                };
            }
            else if (request.ProviderQuery is CouchbaseQueryDefinition providerDef)
            {
                statement = $"SELECT RAW COUNT(*) FROM ({providerDef.Statement}) AS sub";
                definition = new CouchbaseQueryDefinition(statement)
                {
                    Parameters = providerDef.Parameters
                };
            }
            else if (!string.IsNullOrWhiteSpace(request.RawQuery))
            {
                statement = $"SELECT RAW COUNT(*) FROM ({request.RawQuery}) AS sub";
            }
            else
            {
                statement = $"SELECT RAW COUNT(*) FROM {fullKeyspace}";
            }

            var result = await ExecuteScalarQueryAsync<long>(ctx, statement, definition, ct);
            return CountResult.Exact(result);
        }, ct);

    public Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.upsert");
            act?.SetTag("entity", typeof(TEntity).FullName);
            var ctx = await ResolveCollection(ct);
            PrepareEntityForStorage(model);
            var key = GetKey(model.Id);
            var options = new UpsertOptions().CancellationToken(ct);
            if (_kvDurability is { } durability)
            {
                options.Durability(durability);
            }

            try
            {
                await ctx.Collection.UpsertAsync(key, model, options);
            }
            catch (global::Couchbase.Core.Exceptions.UnambiguousTimeoutException ex) when (IsCollectionNotFound(ex))
            {
                await EnsureCollection(ctx, ct);
                await ctx.Collection.UpsertAsync(key, model, options);
            }

            _logger?.LogDebug("Couchbase upsert {Entity} id={Id}", typeof(TEntity).Name, key);
            return model;
        }, ct);

    public Task<bool> Delete(TKey id, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.delete");
            act?.SetTag("entity", typeof(TEntity).FullName);
            var ctx = await ResolveCollection(ct);
            try
            {
                var options = new RemoveOptions().CancellationToken(ct);
                if (_kvDurability is { } durability)
                {
                    options.Durability(durability);
                }
                await ctx.Collection.RemoveAsync(GetKey(id), options);
                return true;
            }
            catch (DocumentNotFoundException)
            {
                return false;
            }
        }, ct);

    public Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.bulk.upsert");
            act?.SetTag("entity", typeof(TEntity).FullName);
            var ctx = await ResolveCollection(ct);
            var items = models as ICollection<TEntity> ?? models.ToArray();
            var upsertTasks = new List<Task>(items.Count);
            foreach (var model in items)
            {
                ct.ThrowIfCancellationRequested();
                PrepareEntityForStorage(model);
                var key = GetKey(model.Id);
                var options = new UpsertOptions().CancellationToken(ct);
                if (_kvDurability is { } durability)
                {
                    options.Durability(durability);
                }
                upsertTasks.Add(ctx.Collection.UpsertAsync(key, model, options));
            }

            await Task.WhenAll(upsertTasks);
            _logger?.LogInformation("Couchbase bulk upsert {Entity} count={Count}", typeof(TEntity).Name, items.Count);
            return items.Count;
        }, ct);

    public Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.bulk.delete");
            act?.SetTag("entity", typeof(TEntity).FullName);
            var ctx = await ResolveCollection(ct);
            var keys = ids as ICollection<TKey> ?? ids.ToArray();
            var tasks = new List<Task<bool>>(keys.Count);
            foreach (var id in keys)
            {
                ct.ThrowIfCancellationRequested();
                tasks.Add(Remove(ctx.Collection, GetKey(id), ct));
            }

            var results = await Task.WhenAll(tasks);
            var deleted = results.Count(static x => x);
            _logger?.LogInformation("Couchbase bulk delete {Entity} count={Count}", typeof(TEntity).Name, deleted);
            return deleted;
        }, ct);

    public Task<int> DeleteAll(CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var ctx = await ResolveCollection(ct);
            var statement = $"DELETE FROM `{ctx.BucketName}`.`{ctx.ScopeName}`.`{ctx.CollectionName}` RETURNING META().id";
            var count = 0;
            await foreach (var _ in ExecuteQueryAsync<dynamic>(ctx, statement, null, null, ct))
            {
                count++;
            }
            return count;
        }, ct);

    public Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var ctx = await ResolveCollection(ct);
            var statement = $"DELETE FROM `{ctx.BucketName}`.`{ctx.ScopeName}`.`{ctx.CollectionName}` RETURNING META().id";
            var count = 0L;
            await foreach (var _ in ExecuteQueryAsync<dynamic>(ctx, statement, null, null, ct))
            {
                count++;
            }
            // No fast path available - bucket flush requires admin permissions
            // Optimized and Fast both use same implementation (Safe)
            return count;
        }, ct);

    public IBatchSet<TEntity, TKey> CreateBatch() => new CouchbaseBatch(this);

    public Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
        => ExecuteWithReadinessAsync(async () =>
        {
            var ctx = await ResolveCollection(ct);
            switch (instruction.Name)
            {
                case DataInstructions.EnsureCreated:
                    await EnsureCollection(ctx, ct);
                    return (TResult)(object)true;
                case DataInstructions.Clear:
                    var deleted = await DeleteAll(ct);
                    return (TResult)(object)deleted;
                default:
                    throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by Couchbase adapter.");
            }
        }, ct);

    private async Task EnsureCollection(CouchbaseCollectionContext ctx, CancellationToken ct)
    {
        var manager = ctx.Bucket.Collections;
        if (!string.Equals(ctx.ScopeName, "_default", StringComparison.Ordinal))
        {
            try
            {
                await manager.CreateScopeAsync(ctx.ScopeName);
                _logger?.LogDebug("Created Couchbase scope: {Scope}", ctx.ScopeName);
            }
            catch (CouchbaseException ex) when (IsAlreadyExists(ex))
            {
                _logger?.LogDebug("Couchbase scope {Scope} already exists", ctx.ScopeName);
            }
        }

        try
        {
            await manager.CreateCollectionAsync(ctx.ScopeName, ctx.CollectionName, new CreateCollectionSettings());
            _logger?.LogInformation("Created Couchbase collection: {Collection} in scope {Scope}", ctx.CollectionName, ctx.ScopeName);

            // Wait for collection to be ready for N1QL queries
            await Task.Delay(2000, ct);
            _logger?.LogDebug("Collection {Collection} ready for queries", ctx.CollectionName);
        }
        catch (CouchbaseException ex) when (IsAlreadyExists(ex))
        {
            _logger?.LogDebug("Couchbase collection {Collection} already exists", ctx.CollectionName);
        }

        // Ensure a primary index exists on the collection so N1QL queries don't fail with
        // "No index available". The Couchbase server only auto-creates the primary index on the
        // bucket's _default collection — every named collection (or custom-named bucket default)
        // needs an explicit CREATE PRIMARY INDEX. This is idempotent via IF NOT EXISTS.
        await EnsurePrimaryIndex(ctx, ct);
    }

    private async Task EnsurePrimaryIndex(CouchbaseCollectionContext ctx, CancellationToken ct)
    {
        var bucket = QuoteIdentifier(ctx.BucketName);
        var scope = QuoteIdentifier(ctx.ScopeName);
        var collection = QuoteIdentifier(ctx.CollectionName);
        var keyspace = $"{bucket}.{scope}.{collection}";

        // Two-step: try CREATE PRIMARY INDEX. If it already exists, Couchbase returns error 4300.
        // We retry briefly because the collection may not yet be registered with N1QL right
        // after creation.
        const int maxAttempts = 5;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                await ctx.Cluster.QueryAsync<dynamic>($"CREATE PRIMARY INDEX ON {keyspace}");
                _logger?.LogDebug("Primary index created on {Keyspace}", keyspace);
                break;
            }
            catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                                       || ex.Message.Contains("4300"))
            {
                _logger?.LogDebug("Primary index already exists on {Keyspace}", keyspace);
                break;
            }
            catch (Exception ex) when (attempt < maxAttempts - 1)
            {
                _logger?.LogDebug(ex, "CREATE PRIMARY INDEX attempt {Attempt}/{Max} failed on {Keyspace}, retrying", attempt + 1, maxAttempts, keyspace);
                await Task.Delay(1000, ct);
            }
        }

        // Wait for the index to come online before returning.
        for (int attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                var probe = await ctx.Cluster.QueryAsync<dynamic>(
                    $"SELECT RAW state FROM system:indexes WHERE bucket_id = $bucket AND scope_id = $scope AND keyspace_id = $collection AND is_primary = true",
                    options => options.Parameter("bucket", ctx.BucketName)
                                       .Parameter("scope", ctx.ScopeName)
                                       .Parameter("collection", ctx.CollectionName));
                var states = new List<string>();
                await foreach (var row in probe)
                {
                    states.Add(row?.ToString() ?? "");
                }
                if (states.Count > 0 && states.All(s => string.Equals(s, "online", StringComparison.OrdinalIgnoreCase)))
                {
                    _logger?.LogDebug("Primary index online on {Keyspace}", keyspace);
                    return;
                }
            }
            catch
            {
                // System catalog query may transiently fail during indexer warm-up.
            }
            await Task.Delay(500, ct);
        }
        _logger?.LogWarning("Primary index on {Keyspace} did not report online after 15s; subsequent queries may fail", keyspace);
    }

    private static string QuoteIdentifier(string name) => "`" + name.Replace("`", "``") + "`";

    private static bool IsAlreadyExists(CouchbaseException ex)
        => ex.Context?.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true;

    public async Task EnsureHealthy(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var collectionName = StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        var ctx = await _provider.GetCollectionContext(collectionName, ct);
        await EnsureCollection(ctx, ct);
    }

    public void InvalidateHealth()
    {
        // No-op: collection health is verified on demand via the provider.
    }

    private async Task<IReadOnlyList<TEntity>> ExecuteQuery(CouchbaseCollectionContext ctx, string statement, CouchbaseQueryDefinition? definition, DataQueryOptions? options, CancellationToken ct)
    {
        var rows = new List<TEntity>();
        await foreach (var row in ExecuteQueryAsync<TEntity>(ctx, statement, definition, options, ct))
        {
            rows.Add(row);
        }
        return rows;
    }

    private async Task<T> ExecuteScalarQueryAsync<T>(CouchbaseCollectionContext ctx, string statement, CouchbaseQueryDefinition? definition, CancellationToken ct)
    {
        await foreach (var row in ExecuteQueryAsync<T>(ctx, statement, definition, null, ct))
        {
            return row;
        }
        return default!;
    }

    private async IAsyncEnumerable<T> ExecuteQueryAsync<T>(CouchbaseCollectionContext ctx, string statement, CouchbaseQueryDefinition? definition, DataQueryOptions? options, [EnumeratorCancellation] CancellationToken ct)
    {
        var finalStatement = statement;
        if (options is not null)
        {
            var (offset, limit) = ComputeSkipTake(options);
            finalStatement = $"{finalStatement} LIMIT {limit} OFFSET {offset}";
        }

        var queryOptions = new QueryOptions();
        var timeout = definition?.Timeout ?? _options.QueryTimeout;
        if (timeout > TimeSpan.Zero)
        {
            queryOptions.Timeout(timeout);
        }
        queryOptions.CancellationToken(ct);
        // Couchbase N1QL defaults to not_bounded scan consistency — queries can return stale
        // results immediately after a mutation. RequestPlus blocks the query until the indexer
        // has caught up with the latest write. The cost is per-query latency, but the alternative
        // is unpredictable read-after-write behaviour that breaks both EntityController semantics
        // and any meaningful test assertion.
        queryOptions.ScanConsistency(global::Couchbase.Query.QueryScanConsistency.RequestPlus);
        if (definition?.Parameters is { Count: > 0 })
        {
            foreach (var parameter in definition.Parameters)
            {
                var value = parameter.Value ?? DBNull.Value;
                queryOptions.Parameter(parameter.Key, value);
            }
        }

        global::Couchbase.Query.IQueryResult<T> result;
        try
        {
            result = await ctx.Cluster.QueryAsync<T>(finalStatement, queryOptions);
        }
        catch (global::Couchbase.Core.Exceptions.IndexFailureException ex) when (ex.Message.Contains("Keyspace not found"))
        {
            // Extract collection name from the error message or statement
            // Try to create the collection and retry the query once
            await EnsureCollection(ctx, ct);

            // Retry the query after creating the collection
            result = await ctx.Cluster.QueryAsync<T>(finalStatement, queryOptions);
        }

        await foreach (var row in result)
        {
            yield return row;
        }
    }


    private static bool IsCollectionNotFound(Exception ex)
    {
        return ex is global::Couchbase.Core.Exceptions.UnambiguousTimeoutException timeout &&
               timeout.Context?.ToString()?.Contains("CollectionNotFound") == true;
    }

    private (int offset, int limit) ComputeSkipTake(DataQueryOptions? options)
    {
        // Check if pagination is active
        var hasPagination = options?.HasPagination ?? false;

        if (!hasPagination)
        {
            // No pagination - return full result set without applying default page size
            return (0, int.MaxValue);
        }

        // Pagination is active - apply default fallback only. Per ADR no adapter-side cap.
        var page = options?.Page is int p && p > 0 ? p : 1;
        var sizeReq = options?.PageSize;
        var size = sizeReq is int ps && ps > 0 ? ps : _options.DefaultPageSize;
        var offset = (page - 1) * size;
        return (offset, size);
    }

    private void PrepareEntityForStorage(TEntity entity)
    {
        if (!_optimizationInfo.IsOptimized || typeof(TKey) != typeof(string))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_optimizationInfo.IdPropertyName))
        {
            return;
        }

        var prop = typeof(TEntity).GetProperty(_optimizationInfo.IdPropertyName);
        if (prop is null || prop.PropertyType != typeof(string))
        {
            return;
        }

        if (prop.GetValue(entity) is string value && Guid.TryParse(value, out var guid))
        {
            prop.SetValue(entity, guid.ToString("N", CultureInfo.InvariantCulture));
        }
    }

    private static string GetKey(TKey id)
        => id switch
        {
            string str => str,
            Guid guid => guid.ToString("N", CultureInfo.InvariantCulture),
            _ => Convert.ToString(id, CultureInfo.InvariantCulture) ?? throw new InvalidOperationException("Unable to convert key to string.")
        };

    private async Task<bool> Remove(ICouchbaseCollection collection, string key, CancellationToken ct)
    {
        try
        {
            var options = new RemoveOptions().CancellationToken(ct);
            if (_kvDurability is { } durability)
            {
                options.Durability(durability);
            }
            await collection.RemoveAsync(key, options);
            return true;
        }
        catch (DocumentNotFoundException)
        {
            return false;
        }
    }

    private sealed class CouchbaseBatch : IBatchSet<TEntity, TKey>
    {
        private readonly CouchbaseRepository<TEntity, TKey> _repo;
        private readonly List<TEntity> _upserts = new();
        private readonly List<TKey> _deletes = new();
        private readonly List<(TKey id, Action<TEntity> mutate)> _mutations = new();

        public CouchbaseBatch(CouchbaseRepository<TEntity, TKey> repo) => _repo = repo;

        public IBatchSet<TEntity, TKey> Add(TEntity entity)
        {
            _upserts.Add(entity);
            return this;
        }

        public IBatchSet<TEntity, TKey> Update(TEntity entity)
            => Add(entity);

        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate)
        {
            _mutations.Add((id, mutate));
            return this;
        }

        public IBatchSet<TEntity, TKey> Delete(TKey id)
        {
            _deletes.Add(id);
            return this;
        }

        public IBatchSet<TEntity, TKey> Clear()
        {
            _upserts.Clear();
            _deletes.Clear();
            _mutations.Clear();
            return this;
        }

        public async Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            if (options?.RequireAtomic == true)
            {
                return await SaveAtomic(ct);
            }

            var added = 0;
            var updated = 0;
            var deleted = 0;

            foreach (var entity in _upserts)
            {
                await _repo.Upsert(entity, ct);
                added++;
            }

            foreach (var (id, mutate) in _mutations)
            {
                var current = await _repo.Get(id, ct);
                if (current is null) continue;
                mutate(current);
                await _repo.Upsert(current, ct);
                updated++;
            }

            foreach (var id in _deletes)
            {
                if (await _repo.Delete(id, ct))
                {
                    deleted++;
                }
            }

            Clear();
            return new BatchResult(added, updated, deleted);
        }

        private async Task<BatchResult> SaveAtomic(CancellationToken ct)
        {
            var ctx = await _repo.ResolveCollection(ct);
            var added = 0;
            var updated = 0;
            var deleted = 0;

            try
            {
                await ctx.Cluster.Transactions.RunAsync(async attempt =>
                {
                    foreach (var entity in _upserts)
                    {
                        _repo.PrepareEntityForStorage(entity);
                        var key = GetKey(entity.Id);
                        try
                        {
                            var existing = await attempt.GetAsync(ctx.Collection, key);
                            await attempt.ReplaceAsync(existing, entity);
                            updated++;
                        }
                        catch (TransactionFailedException ex) when (ex.InnerException is DocumentNotFoundException)
                        {
                            await attempt.InsertAsync(ctx.Collection, key, entity);
                            added++;
                        }
                    }

                    foreach (var (id, mutate) in _mutations)
                    {
                        try
                        {
                            var key = GetKey(id);
                            var current = await attempt.GetAsync(ctx.Collection, key);
                            var entity = current.ContentAs<TEntity>();
                            if (entity is null)
                            {
                                continue;
                            }
                            mutate(entity);
                            _repo.PrepareEntityForStorage(entity);
                            await attempt.ReplaceAsync(current, entity);
                            updated++;
                        }
                        catch (TransactionFailedException ex) when (ex.InnerException is DocumentNotFoundException)
                        {
                            // skip missing documents for mutation updates
                        }
                    }

                    foreach (var id in _deletes)
                    {
                        try
                        {
                            var key = GetKey(id);
                            var current = await attempt.GetAsync(ctx.Collection, key);
                            await attempt.RemoveAsync(current);
                            deleted++;
                        }
                        catch (TransactionFailedException ex) when (ex.InnerException is DocumentNotFoundException)
                        {
                            // ignore missing rows
                        }
                    }
                });
            }
            catch (TransactionFailedException ex)
            {
                throw new NotSupportedException("Couchbase cluster failed to execute atomic batch transaction.", ex);
            }

            Clear();
            return new BatchResult(added, updated, deleted);
        }
    }
}

