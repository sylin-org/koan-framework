using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Couchbase;
using Couchbase.KeyValue;
using Couchbase.Management.Collections;
using Couchbase.Query;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core.Configuration;
using Koan.Data.Core.Optimization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Couchbase;

internal sealed class CouchbaseRepository<TEntity, TKey> :
    IDataRepositoryWithOptions<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>,
    IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly CouchbaseClusterProvider _provider;
    private readonly IServiceProvider _sp;
    private readonly ILogger? _logger;
    private readonly CouchbaseOptions _options;
    private readonly StorageOptimizationInfo _optimizationInfo;
    private string _collectionName;

    public CouchbaseRepository(CouchbaseClusterProvider provider, IStorageNameResolver resolver, IServiceProvider sp, IOptions<CouchbaseOptions> options)
    {
        _provider = provider;
        _sp = sp;
        _logger = sp.GetService<ILogger<CouchbaseRepository<TEntity, TKey>>>();
        _options = options.Value;
        _optimizationInfo = sp.GetStorageOptimization<TEntity, TKey>();
        _collectionName = StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        ArgumentNullException.ThrowIfNull(resolver);
    }

    public QueryCapabilities Capabilities => QueryCapabilities.String;
    public WriteCapabilities Writes => WriteCapabilities.BulkUpsert | WriteCapabilities.BulkDelete;

    private async ValueTask<CouchbaseCollectionContext> ResolveCollectionAsync(CancellationToken ct)
    {
        var desired = StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        if (!string.Equals(_collectionName, desired, StringComparison.Ordinal))
        {
            _collectionName = desired;
        }
        return await _provider.GetCollectionContextAsync(_collectionName, ct).ConfigureAwait(false);
    }

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.get");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var ctx = await ResolveCollectionAsync(ct).ConfigureAwait(false);
        try
        {
            var result = await ctx.Collection.GetAsync(GetKey(id), new GetOptions().CancellationToken(ct)).ConfigureAwait(false);
            return result.ContentAs<TEntity>();
        }
        catch (DocumentNotFoundException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
        => await QueryInternalAsync(query, null, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
        => await QueryInternalAsync(query, options, ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<TEntity>> QueryInternalAsync(object? query, DataQueryOptions? options, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ctx = await ResolveCollectionAsync(ct).ConfigureAwait(false);
        var definition = query switch
        {
            CouchbaseQueryDefinition def => def,
            string statement when !string.IsNullOrWhiteSpace(statement) => new CouchbaseQueryDefinition(statement),
            _ => null
        };

        var statement = definition?.Statement ??
            $"SELECT RAW doc FROM `{ctx.BucketName}`.`{ctx.ScopeName}`.`{ctx.CollectionName}` AS doc";

        return await ExecuteQueryAsync(ctx, statement, definition, options, ct).ConfigureAwait(false);
    }

    public async Task<int> CountAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var ctx = await ResolveCollectionAsync(ct).ConfigureAwait(false);
        string statement;
        CouchbaseQueryDefinition? def = null;
        if (query is CouchbaseQueryDefinition definition)
        {
            def = definition;
            statement = $"SELECT RAW COUNT(*) FROM ({definition.Statement}) AS sub";
        }
        else if (query is string str && !string.IsNullOrWhiteSpace(str))
        {
            statement = $"SELECT RAW COUNT(*) FROM ({str}) AS sub";
        }
        else
        {
            statement = $"SELECT RAW COUNT(*) FROM `{ctx.BucketName}`.`{ctx.ScopeName}`.`{ctx.CollectionName}`";
        }

        var result = await ExecuteScalarQueryAsync<long>(ctx, statement, def, ct).ConfigureAwait(false);
        return (int)result;
    }

    public Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
        => throw new NotSupportedException("Couchbase adapter does not support LINQ predicates yet. Provide a N1QL query via string or CouchbaseQueryDefinition.");

    public async Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.upsert");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var ctx = await ResolveCollectionAsync(ct).ConfigureAwait(false);
        PrepareEntityForStorage(model);
        var key = GetKey(model.Id);
        await ctx.Collection.UpsertAsync(key, model, new UpsertOptions().CancellationToken(ct)).ConfigureAwait(false);
        _logger?.LogDebug("Couchbase upsert {Entity} id={Id}", typeof(TEntity).Name, key);
        return model;
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.delete");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var ctx = await ResolveCollectionAsync(ct).ConfigureAwait(false);
        try
        {
            await ctx.Collection.RemoveAsync(GetKey(id), new RemoveOptions().CancellationToken(ct)).ConfigureAwait(false);
            return true;
        }
        catch (DocumentNotFoundException)
        {
            return false;
        }
    }

    public async Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.bulk.upsert");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var ctx = await ResolveCollectionAsync(ct).ConfigureAwait(false);
        var count = 0;
        foreach (var model in models)
        {
            ct.ThrowIfCancellationRequested();
            PrepareEntityForStorage(model);
            await ctx.Collection.UpsertAsync(GetKey(model.Id), model, new UpsertOptions().CancellationToken(ct)).ConfigureAwait(false);
            count++;
        }
        return count;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var act = CouchbaseTelemetry.Activity.StartActivity("couchbase.bulk.delete");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var ctx = await ResolveCollectionAsync(ct).ConfigureAwait(false);
        var count = 0;
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await ctx.Collection.RemoveAsync(GetKey(id), new RemoveOptions().CancellationToken(ct)).ConfigureAwait(false);
                count++;
            }
            catch (DocumentNotFoundException)
            {
                // ignore
            }
        }
        return count;
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var ctx = await ResolveCollectionAsync(ct).ConfigureAwait(false);
        var statement = $"DELETE FROM `{ctx.BucketName}`.`{ctx.ScopeName}`.`{ctx.CollectionName}` RETURNING META().id";
        var result = await ExecuteQueryAsync<dynamic>(ctx, statement, null, null, ct).ConfigureAwait(false);
        return result.Count;
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new CouchbaseBatch(this);

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        var ctx = await ResolveCollectionAsync(ct).ConfigureAwait(false);
        switch (instruction.Name)
        {
            case DataInstructions.EnsureCreated:
                await EnsureCollectionAsync(ctx, ct).ConfigureAwait(false);
                return (TResult)(object)true;
            case DataInstructions.Clear:
                var deleted = await DeleteAllAsync(ct).ConfigureAwait(false);
                return (TResult)(object)deleted;
            default:
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by Couchbase adapter.");
        }
    }

    private async Task EnsureCollectionAsync(CouchbaseCollectionContext ctx, CancellationToken ct)
    {
        var manager = ctx.Bucket.Collections;
        if (!string.Equals(ctx.ScopeName, "_default", StringComparison.Ordinal))
        {
            try { await manager.CreateScopeAsync(ctx.ScopeName, ct).ConfigureAwait(false); }
            catch (CouchbaseException ex) when (IsAlreadyExists(ex)) { }
        }

        var spec = new CollectionSpec(ctx.ScopeName, ctx.CollectionName);
        try { await manager.CreateCollectionAsync(spec, ct).ConfigureAwait(false); }
        catch (CouchbaseException ex) when (IsAlreadyExists(ex)) { }
    }

    private static bool IsAlreadyExists(CouchbaseException ex)
        => ex.Context?.Message?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true;

    private async Task<IReadOnlyList<TEntity>> ExecuteQueryAsync(CouchbaseCollectionContext ctx, string statement, CouchbaseQueryDefinition? definition, DataQueryOptions? options, CancellationToken ct)
    {
        var rows = new List<TEntity>();
        await foreach (var row in ExecuteQueryAsync<TEntity>(ctx, statement, definition, options, ct).ConfigureAwait(false))
        {
            rows.Add(row);
        }
        return rows;
    }

    private async Task<T> ExecuteScalarQueryAsync<T>(CouchbaseCollectionContext ctx, string statement, CouchbaseQueryDefinition? definition, CancellationToken ct)
    {
        await foreach (var row in ExecuteQueryAsync<T>(ctx, statement, definition, null, ct).ConfigureAwait(false))
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
        if (definition?.Parameters is { Count: > 0 })
        {
            queryOptions.NamedParameters(definition.Parameters);
        }

        var result = await ctx.Cluster.QueryAsync<T>(finalStatement, queryOptions).ConfigureAwait(false);
        await foreach (var row in result)
        {
            yield return row;
        }
    }

    private (int offset, int limit) ComputeSkipTake(DataQueryOptions? options)
    {
        var page = options?.Page is int p && p > 0 ? p : 1;
        var sizeReq = options?.PageSize;
        var size = sizeReq is int ps && ps > 0 ? ps : _options.DefaultPageSize;
        if (size > _options.MaxPageSize) size = _options.MaxPageSize;
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

        public async Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default)
        {
            if (options?.RequireAtomic == true)
            {
                throw new NotSupportedException("Couchbase adapter does not yet support transactional batches.");
            }

            var added = 0;
            var updated = 0;
            var deleted = 0;

            foreach (var entity in _upserts)
            {
                await _repo.UpsertAsync(entity, ct).ConfigureAwait(false);
                added++;
            }

            foreach (var (id, mutate) in _mutations)
            {
                var current = await _repo.GetAsync(id, ct).ConfigureAwait(false);
                if (current is null) continue;
                mutate(current);
                await _repo.UpsertAsync(current, ct).ConfigureAwait(false);
                updated++;
            }

            foreach (var id in _deletes)
            {
                if (await _repo.DeleteAsync(id, ct).ConfigureAwait(false))
                {
                    deleted++;
                }
            }

            Clear();
            return new BatchResult(added, updated, deleted);
        }
    }
}
