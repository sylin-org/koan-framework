using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Instructions;
using Koan.Data.Core.Schema;
using Microsoft.Extensions.Logging;

namespace Koan.Cache.Decorators;

internal sealed class CachedRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IDataRepositoryWithOptions<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
    IStringQueryRepository<TEntity, TKey>,
    IStringQueryRepositoryWithOptions<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    IInstructionExecutor<TEntity>,
    ISchemaHealthContributor<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IDataRepository<TEntity, TKey> _inner;
    private readonly IDataRepositoryWithOptions<TEntity, TKey>? _withOptions;
    private readonly ILinqQueryRepository<TEntity, TKey>? _linq;
    private readonly ILinqQueryRepositoryWithOptions<TEntity, TKey>? _linqWithOptions;
    private readonly IStringQueryRepository<TEntity, TKey>? _stringQuery;
    private readonly IStringQueryRepositoryWithOptions<TEntity, TKey>? _stringQueryWithOptions;
    private readonly IInstructionExecutor<TEntity>? _instructionExecutor;
    private readonly ISchemaHealthContributor<TEntity, TKey>? _schemaContributor;
    private readonly IQueryCapabilities? _queryCapabilitiesSource;
    private readonly IWriteCapabilities? _writeCapabilitiesSource;
    private readonly ICacheClient _cacheClient;
    private readonly CachePolicyDescriptor _entityPolicy;
    private readonly CacheKeyTemplate _entityTemplate;
    private readonly ILogger<CachedRepository<TEntity, TKey>> _logger;
    private readonly Func<TEntity, TKey> _keyAccessor;
    private readonly string _entityName;

    public CachedRepository(
        IDataRepository<TEntity, TKey> inner,
        ICacheClient cacheClient,
        CachePolicyDescriptor entityPolicy,
        ILogger<CachedRepository<TEntity, TKey>> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cacheClient = cacheClient ?? throw new ArgumentNullException(nameof(cacheClient));
        _entityPolicy = entityPolicy ?? throw new ArgumentNullException(nameof(entityPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _entityTemplate = CacheKeyTemplate.For(_entityPolicy.KeyTemplate);
        _withOptions = inner as IDataRepositoryWithOptions<TEntity, TKey>;
        _linq = inner as ILinqQueryRepository<TEntity, TKey>;
        _linqWithOptions = inner as ILinqQueryRepositoryWithOptions<TEntity, TKey>;
        _stringQuery = inner as IStringQueryRepository<TEntity, TKey>;
        _stringQueryWithOptions = inner as IStringQueryRepositoryWithOptions<TEntity, TKey>;
        _instructionExecutor = inner as IInstructionExecutor<TEntity>;
        _schemaContributor = inner as ISchemaHealthContributor<TEntity, TKey>;
        _queryCapabilitiesSource = inner as IQueryCapabilities;
    _writeCapabilitiesSource = inner as IWriteCapabilities;
    _keyAccessor = static entity => ((IEntity<TKey>)entity).Id;
        _entityName = typeof(TEntity).Name;
    }

    public QueryCapabilities Capabilities => _queryCapabilitiesSource?.Capabilities ?? QueryCapabilities.None;

    public WriteCapabilities Writes => _writeCapabilitiesSource?.Writes ?? WriteCapabilities.None;

    public Task<CountResult> CountAsync(CountRequest<TEntity> request, CancellationToken ct = default)
        => _inner.CountAsync(request, ct);

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        if (_entityPolicy.Strategy is CacheStrategy.NoCache or CacheStrategy.SetOnly or CacheStrategy.Invalidate)
        {
            return await _inner.GetAsync(id, ct).ConfigureAwait(false);
        }

        if (!TryBuildEntityKey(null, id, out var key))
        {
            return await _inner.GetAsync(id, ct).ConfigureAwait(false);
        }

        var options = _entityPolicy.ToOptions();
        switch (_entityPolicy.Strategy)
        {
            case CacheStrategy.GetOrSet:
                return await _cacheClient.GetOrAddAsync<TEntity>(key, async innerCt =>
                {
                    var value = await _inner.GetAsync(id, innerCt).ConfigureAwait(false);
                    return value;
                }, options, ct).ConfigureAwait(false);

            case CacheStrategy.GetOnly:
                {
                    var cached = await _cacheClient.GetAsync<TEntity>(key, options, ct).ConfigureAwait(false);
                    if (cached is not null)
                    {
                        return cached;
                    }

                    return await _inner.GetAsync(id, ct).ConfigureAwait(false);
                }

            default:
                return await _inner.GetAsync(id, ct).ConfigureAwait(false);
        }
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
        => _inner.QueryAsync(query, ct);

    public Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        if (_withOptions is null)
        {
            return _inner.QueryAsync(query, ct);
        }

        return _withOptions.QueryAsync(query, options, ct);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(string query, CancellationToken ct = default)
    {
        if (_stringQuery is null)
        {
            throw new NotSupportedException($"Repository for {_entityName} does not support string queries.");
        }

        return _stringQuery.QueryAsync(query, ct);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(string query, DataQueryOptions? options, CancellationToken ct = default)
    {
        if (_stringQueryWithOptions is not null)
        {
            return _stringQueryWithOptions.QueryAsync(query, options, ct);
        }

        if (_stringQuery is not null)
        {
            return _stringQuery.QueryAsync(query, ct);
        }

        throw new NotSupportedException($"Repository for {_entityName} does not support string queries.");
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(string query, object? parameters, CancellationToken ct = default)
    {
        if (_stringQueryWithOptions is not null)
        {
            return _stringQueryWithOptions.QueryAsync(query, parameters, null, ct);
        }

        throw new NotSupportedException($"Repository for {_entityName} does not support parameterized string queries.");
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(string query, object? parameters, DataQueryOptions? options, CancellationToken ct = default)
    {
        if (_stringQueryWithOptions is not null)
        {
            return _stringQueryWithOptions.QueryAsync(query, parameters, options, ct);
        }

        throw new NotSupportedException($"Repository for {_entityName} does not support parameterized string queries.");
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        if (_linq is null)
        {
            throw new NotSupportedException($"Repository for {_entityName} does not support LINQ queries.");
        }

        return _linq.QueryAsync(predicate, ct);
    }

    public Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
    {
        if (_linqWithOptions is not null)
        {
            return _linqWithOptions.QueryAsync(predicate, options, ct);
        }

        if (_linq is not null)
        {
            return _linq.QueryAsync(predicate, ct);
        }

        throw new NotSupportedException($"Repository for {_entityName} does not support LINQ queries.");
    }

    public async Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
        var result = await _inner.UpsertAsync(model, ct).ConfigureAwait(false);
        await HandleWriteAsync(result, ct).ConfigureAwait(false);
        return result;
    }

    public async Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        var materialized = models as IList<TEntity> ?? models.ToList();
        var updated = await _inner.UpsertManyAsync(materialized, ct).ConfigureAwait(false);
        await HandleWriteAsync(materialized, ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        var deleted = await _inner.DeleteAsync(id, ct).ConfigureAwait(false);
        if (deleted)
        {
            await RemoveAsync(id, ct).ConfigureAwait(false);
        }

        return deleted;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        var materialized = ids as IList<TKey> ?? ids.ToList();
        var count = await _inner.DeleteManyAsync(materialized, ct).ConfigureAwait(false);
        if (count > 0)
        {
            foreach (var id in materialized)
            {
                await RemoveAsync(id, ct).ConfigureAwait(false);
            }
        }

        return count;
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        var count = await _inner.DeleteAllAsync(ct).ConfigureAwait(false);
        if (count > 0)
        {
            _logger.LogDebug("DeleteAll detected for {Entity}. Cached entries cannot be enumerated automatically; downstream policies should prefer tag-based invalidation.", _entityName);
        }

        return count;
    }

    public async Task<long> RemoveAllAsync(RemoveStrategy strategy, CancellationToken ct = default)
    {
        var removed = await _inner.RemoveAllAsync(strategy, ct).ConfigureAwait(false);
        if (removed > 0)
        {
            _logger.LogDebug("RemoveAll({Strategy}) invoked for {Entity}; cached entries should be invalidated via tags.", strategy, _entityName);
        }

        return removed;
    }

    public IBatchSet<TEntity, TKey> CreateBatch()
    {
        var batch = _inner.CreateBatch();
        if (batch is null)
        {
            throw new InvalidOperationException($"Repository for {_entityName} returned a null batch set.");
        }

        return new CachingBatchSet(this, batch);
    }

    public async Task<TResult> ExecuteAsync<TResult>(Instruction instruction, CancellationToken ct = default)
    {
        if (_instructionExecutor is null)
        {
            throw new NotSupportedException($"Repository for {_entityName} does not support instruction execution.");
        }

        return await _instructionExecutor.ExecuteAsync<TResult>(instruction, ct).ConfigureAwait(false);
    }

    public Task EnsureHealthyAsync(CancellationToken ct)
    {
        if (_schemaContributor is null)
        {
            return Task.CompletedTask;
        }

        return _schemaContributor.EnsureHealthyAsync(ct);
    }

    public void InvalidateHealth()
    {
        _schemaContributor?.InvalidateHealth();
    }

    private async ValueTask HandleWriteAsync(IEnumerable<TEntity> entities, CancellationToken ct)
    {
        foreach (var entity in entities)
        {
            await HandleWriteAsync(entity, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask HandleWriteAsync(TEntity entity, CancellationToken ct)
    {
        if (_entityPolicy.Strategy is CacheStrategy.NoCache)
        {
            return;
        }

        var key = ResolveKey(entity);
        if (key is null)
        {
            return;
        }

        switch (_entityPolicy.Strategy)
        {
            case CacheStrategy.GetOrSet:
            case CacheStrategy.SetOnly:
                var options = _entityPolicy.ToOptions();
                await _cacheClient.SetAsync(key.Value, entity, options, ct).ConfigureAwait(false);
                break;
            case CacheStrategy.GetOnly:
            case CacheStrategy.Invalidate:
                await _cacheClient.RemoveAsync(key.Value, ct).ConfigureAwait(false);
                break;
        }
    }

    private async ValueTask RemoveAsync(TKey id, CancellationToken ct)
    {
        if (_entityPolicy.Strategy is CacheStrategy.NoCache or CacheStrategy.SetOnly)
        {
            return;
        }

        if (!TryBuildEntityKey(null, id, out var key))
        {
            return;
        }

        await _cacheClient.RemoveAsync(key, ct).ConfigureAwait(false);
    }

    private CacheKey? ResolveKey(TEntity entity)
    {
        var id = _keyAccessor(entity);
        if (IsDefaultKey(id))
        {
            _logger.LogDebug("Entity of type {Entity} produced a default key; skipping cache interaction.", _entityName);
            return null;
        }

        if (!TryBuildEntityKey(entity, id, out var key))
        {
            return null;
        }

        return key;
    }

    private bool TryBuildEntityKey(TEntity? entity, object? id, out CacheKey key)
    {
        var ambient = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"] = id,
            ["Key"] = id
        };

        if (entity is not null)
        {
            ambient["Entity"] = entity;
        }

        var formatted = _entityTemplate.TryFormat(entity, ambient, out var missingToken);
        if (formatted is null)
        {
            if (missingToken)
            {
                _logger.LogDebug("Cache policy for {Entity} could not resolve key template '{Template}'. Skipping cache interaction.", _entityName, _entityPolicy.KeyTemplate);
            }

            key = default;
            return false;
        }

        key = new CacheKey(formatted);
        return true;
    }

    private static bool IsDefaultKey(TKey key)
        => EqualityComparer<TKey>.Default.Equals(key, default!);

    private sealed class CachingBatchSet : IBatchSet<TEntity, TKey>
    {
        private readonly CachedRepository<TEntity, TKey> _outer;
        private readonly IBatchSet<TEntity, TKey> _inner;
        private readonly List<TEntity> _upserts = new();
        private readonly List<TKey> _deletes = new();
        private readonly HashSet<TKey> _mutations = new();

        public CachingBatchSet(CachedRepository<TEntity, TKey> outer, IBatchSet<TEntity, TKey> inner)
        {
            _outer = outer;
            _inner = inner;
        }

        public IBatchSet<TEntity, TKey> Add(TEntity entity)
        {
            _upserts.Add(entity);
            _inner.Add(entity);
            return this;
        }

        public IBatchSet<TEntity, TKey> Update(TEntity entity)
        {
            _upserts.Add(entity);
            _inner.Update(entity);
            return this;
        }

        public IBatchSet<TEntity, TKey> Delete(TKey id)
        {
            _deletes.Add(id);
            _inner.Delete(id);
            return this;
        }

        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate)
        {
            _mutations.Add(id);
            _inner.Update(id, mutate);
            return this;
        }

        public IBatchSet<TEntity, TKey> Clear()
        {
            _upserts.Clear();
            _deletes.Clear();
            _mutations.Clear();
            _inner.Clear();
            return this;
        }

        public async Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default)
        {
            var result = await _inner.SaveAsync(options, ct).ConfigureAwait(false);

            foreach (var entity in _upserts)
            {
                await _outer.HandleWriteAsync(entity, ct).ConfigureAwait(false);
            }

            foreach (var id in _deletes)
            {
                await _outer.RemoveAsync(id, ct).ConfigureAwait(false);
            }

            foreach (var id in _mutations)
            {
                await _outer.RemoveAsync(id, ct).ConfigureAwait(false);
            }

            _upserts.Clear();
            _deletes.Clear();
            _mutations.Clear();

            return result;
        }
    }
}
