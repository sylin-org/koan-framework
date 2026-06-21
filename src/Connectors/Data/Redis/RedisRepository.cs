using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sorting;
using Koan.Data.Core.Sorting;
using StackExchange.Redis;
using System.Collections.Frozen;
using System.Reflection;
using Newtonsoft.Json;

namespace Koan.Data.Connector.Redis;

/// <summary>
/// Redis key/value store. A "Full floor" adapter under the unified query contract: like the in-memory
/// reference adapter (DATA-0081), it scans the keyspace, materializes entities, and evaluates the entire
/// <see cref="Filter"/> via <see cref="InMemoryFilterEvaluator"/> — so it correctly declares
/// <see cref="FilterSupport.Full"/>. Under ARCH-0084 <c>Full</c> is operator-correctness ("I honour every
/// operator faithfully"), not pushdown efficiency; the full-keyspace scan is a documented cost at scale,
/// not a capability lie (DATA-0101). Honours native TTL: a single-property <c>[Index(Ttl = true)]</c>
/// timestamp expires its key via <c>EXPIREAT</c> (<see cref="DataCaps.Retention"/>.TtlIndex, mirroring
/// Mongo's <c>expireAfterSeconds = 0</c>; DATA-0101).
/// </summary>
internal sealed class RedisRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IQueryRepository<TEntity, TKey>,
    IDescribesCapabilities,
    Abstractions.Instructions.IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IOptions<RedisOptions> _options;
    private readonly IConnectionMultiplexer _muxer;
    private readonly ILogger? _logger;

    public RedisRepository(IOptions<RedisOptions> options, IConnectionMultiplexer muxer, ILoggerFactory? lf)
    { _options = options; _muxer = muxer; _logger = lf?.CreateLogger("Koan.Data.Connector.Redis"); }

    public void Describe(ICapabilities caps) => caps
        .Add(DataCaps.Query.Linq)            // predicate filtering in-memory
        .Add(DataCaps.Write.FastRemove)
        .Add(DataCaps.Retention.TtlIndex)    // native key TTL via [Index(Ttl = true)] -> EXPIREAT (DATA-0101)
        .Add(DataCaps.Query.Filter, FilterSupport.Full);

    // DATA-0101 native TTL. A single-property [Index(Ttl = true)] timestamp drives store-native key expiry
    // (EXPIREAT). Resolved once per closed entity type; null when the entity declares no TTL index, so
    // non-TTL entities keep the single-round-trip SET/MSET write path with zero hot-path cost.
    private static readonly PropertyInfo? TtlProperty = ResolveTtlProperty();

    private static PropertyInfo? ResolveTtlProperty()
    {
        foreach (var idx in Koan.Data.Core.IndexMetadata.GetIndexes(typeof(TEntity)))
            if (idx.Ttl && idx.Properties.Count == 1)
                return idx.Properties[0];
        return null;
    }

    // The absolute UTC instant at which the entity's key should expire, or null to persist indefinitely
    // (a null/absent TTL value is never expired — mirrors Mongo's TTL-index semantics).
    private static DateTime? ReadExpiry(TEntity model)
        => TtlProperty!.GetValue(model) switch
        {
            DateTimeOffset dto => dto.UtcDateTime,
            DateTime dt => dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime(),
            _ => null
        };

    // Atomic single-key write honoring TTL. `SET ... PX` is one round-trip — no SET-then-EXPIRE crash-leak
    // window (where a key could persist with no TTL if the process died between the two commands). A future
    // [Index(Ttl)] instant becomes the key's expiry; a past instant removes the key (already expired); a
    // null/absent value (or a non-TTL type) writes a persistent key, and SET clears any prior TTL (DATA-0101).
    private static async Task WriteWithTtlAsync(IDatabase db, RedisKey key, RedisValue json, TEntity model)
    {
        if (TtlProperty is null || ReadExpiry(model) is not { } expireAt)
        {
            await db.StringSetAsync(key, json);
            return;
        }

        var ttl = expireAt - DateTime.UtcNow;
        if (ttl > TimeSpan.Zero)
            await db.StringSetAsync(key, json, ttl);  // atomic SET PX
        else
            await db.KeyDeleteAsync(key);             // already expired -> no live key
    }

    private string Keyspace()
    {
        var sp = Koan.Core.Hosting.App.AppHost.Current;
        if (sp is not null)
        {
            return Core.Configuration.AdapterNaming.GetOrCompute<TEntity, TKey>(sp);
        }
        // No host yet (early init / direct construction): still route through the resolver with this adapter's
        // announced convention so a generic entity name is grammar-correct (not the mangled typeof(T).Name).
        return StorageNameResolver.Resolve(typeof(TEntity),
            new StorageNameResolver.Convention(StorageNamingStyle.EntityType, "_", NameCasing.AsIs));
    }

    private IDatabase Db() => _muxer.GetDatabase(_options.Value.Database);

    public async Task<TEntity?> Get(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = $"{Keyspace()}:{id}";
        var v = await Db().StringGetAsync(key);
        if (v.IsNullOrEmpty) return null;
    return JsonConvert.DeserializeObject<TEntity>(v!);
    }

    public async Task<IReadOnlyList<TEntity?>> GetMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var idList = ids as IReadOnlyList<TKey> ?? ids.ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        var keyspace = Keyspace();
        var keys = idList.Select(id => (RedisKey)$"{keyspace}:{id}").ToArray();
        var values = await Db().StringGetAsync(keys);

        var results = new TEntity?[idList.Count];
        for (var i = 0; i < values.Length; i++)
        {
            results[i] = values[i].IsNullOrEmpty ? null : JsonConvert.DeserializeObject<TEntity>(values[i]!);
        }

        return results;
    }

    public async Task<RepositoryQueryResult<TEntity>> Query(QueryDefinition query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (all, _) = await ScanAll(page: 1, size: int.MaxValue, ct);
        IEnumerable<TEntity> items = all;
        if (query.Filter is not null)
            items = items.Where(InMemoryFilterEvaluator.Compile<TEntity>(query.Filter));

        var filtered = items as IReadOnlyList<TEntity> ?? items.ToList();
        var totalCount = (long)filtered.Count;

        var sortHandled = RepositoryQueryResult<TEntity>.NoSortHandled;
        IEnumerable<TEntity> ordered = filtered;
        if (query.HasSort)
        {
            ordered = InMemorySorter.Apply(filtered, query.Sort);
            sortHandled = query.Sort.ToFrozenSet();
        }

        var paginationHandled = false;
        if (query.HasPagination)
        {
            var skip = (query.EffectivePage() - 1) * query.EffectivePageSize();
            ordered = ordered.Skip(skip).Take(query.EffectivePageSize());
            paginationHandled = true;
        }

        var list = ordered as IReadOnlyList<TEntity> ?? ordered.ToList();
        return new RepositoryQueryResult<TEntity>
        {
            Items = list,
            TotalCount = totalCount,
            IsEstimate = false,
            SortHandled = sortHandled,
            PaginationHandled = paginationHandled,
        };
    }

    public async Task<CountResult> Count(QueryDefinition query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (all, total) = await ScanAll(page: 1, size: int.MaxValue, ct);
        if (query.Filter is null) return CountResult.Exact(total);
        var count = all.LongCount(InMemoryFilterEvaluator.Compile<TEntity>(query.Filter));
        return CountResult.Exact(count);
    }

    public async Task<TEntity> Upsert(TEntity model, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await WriteWithTtlAsync(Db(), $"{Keyspace()}:{model.Id}", JsonConvert.SerializeObject(model), model);
        return model;
    }

    public async Task<bool> Delete(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = $"{Keyspace()}:{id}";
        return await Db().KeyDeleteAsync(key);
    }

    public async Task<int> UpsertMany(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var arr = models as TEntity[] ?? models.ToArray();
        var keyspace = Keyspace();
        var db = Db();
        if (TtlProperty is null)
        {
            // Fast path for non-TTL types (the common case): one atomic MSET.
            var entries = arr.Select(e => new KeyValuePair<RedisKey, RedisValue>($"{keyspace}:{e.Id}", JsonConvert.SerializeObject(e))).ToArray();
            await db.StringSetAsync(entries);
            return arr.Length;
        }

        // TTL type: MSET cannot carry per-key TTL, so write each key with an atomic SET PX (DATA-0101).
        // Return value is "rows upserted" (matches Mongo) — an already-past expiry still counts as upserted,
        // it is simply expired immediately, exactly as Mongo's expireAfterSeconds=0 expires it shortly after.
        foreach (var e in arr)
            await WriteWithTtlAsync(db, $"{keyspace}:{e.Id}", JsonConvert.SerializeObject(e), e);
        return arr.Length;
    }

    public async Task<int> DeleteMany(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var keys = ids.Select(id => (RedisKey)$"{Keyspace()}:{id}").ToArray();
        return (int)await Db().KeyDeleteAsync(keys);
    }

    public async Task<int> DeleteAll(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (items, _) = await ScanAll(page: 1, size: int.MaxValue, ct);
        var keys = items.Select(e => (RedisKey)$"{Keyspace()}:{e.Id}").ToArray();
        if (keys.Length == 0) return 0;
        return (int)await Db().KeyDeleteAsync(keys);
    }

    public async Task<long> RemoveAll(RemoveStrategy strategy, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (items, _) = await ScanAll(page: 1, size: int.MaxValue, ct);
        var keys = items.Select(e => (RedisKey)$"{Keyspace()}:{e.Id}").ToArray();
        if (keys.Length == 0) return 0;

        // Resolve Optimized strategy based on provider capabilities
        var effectiveStrategy = strategy == RemoveStrategy.Optimized
            ? RemoveStrategy.Fast // this adapter declares write.fastRemove
            : strategy;

        if (effectiveStrategy == RemoveStrategy.Fast)
        {
            // Fast path: UNLINK (async deletion, non-blocking, Redis 4.0+)
            // Deletes keys in background thread, returns immediately
            var db = Db();
            var count = 0L;
            foreach (var key in keys)
            {
                var result = await db.ExecuteAsync("UNLINK", key);
                count += (long)result;
            }
            return count;
        }

        // Safe path: DEL (synchronous deletion)
        // Note: Redis doesn't have hooks, so both paths are similar
        return await Db().KeyDeleteAsync(keys);
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new RedisBatch(this);

    public Task<TResult> ExecuteAsync<TResult>(Abstractions.Instructions.Instruction instruction, CancellationToken ct = default)
    {
        switch (instruction.Name)
        {
            case Abstractions.Instructions.DataInstructions.EnsureCreated:
                // Nothing to create for Redis; consider connection + ping
                return Task.FromResult((TResult)(object)true);
            case Abstractions.Instructions.DataInstructions.Clear:
                return (Task<TResult>)(object)DeleteAll(ct);
            default:
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by Redis adapter for {typeof(TEntity).Name}.");
        }
    }

    private async Task<(List<TEntity> items, int total)> ScanAll(int page, int size, CancellationToken ct)
    {
        // SCAN through the keyspace; StackExchange.Redis: server.Keys is enumerative and can be expensive
        var db = Db();
        var server = _muxer.GetEndPoints().Select(ep => _muxer.GetServer(ep)).FirstOrDefault(s => s.IsConnected);
        if (server is null)
        {
            // Fallback: no server available (e.g., cloud); approximate by key pattern without counts
            var pattern = $"{Keyspace()}:*";
            var keys = new List<RedisKey>();
            var iter = db.ExecuteAsync("SCAN", 0, "MATCH", pattern, "COUNT", 1000);
            // Best-effort: stop after first page since SCAN via Execute is not strongly typed here
            // Consumers should supply filters to avoid huge scans.
            foreach (var k in db.Multiplexer.GetServers().SelectMany(s => s.Keys(db.Database, pattern: pattern)).Take(page * size))
            {
                keys.Add(k);
                if (keys.Count >= page * size) break;
            }
            var pageKeys = keys.Skip((page - 1) * size).Take(size).ToArray();
            var vals = await db.StringGetAsync(pageKeys);
            var list = new List<TEntity>(pageKeys.Length);
            foreach (var v in vals)
            {
                if (v.IsNull) continue;
                var e = JsonConvert.DeserializeObject<TEntity>(v!);
                if (e is not null) list.Add(e);
            }
            return (list, keys.Count);
        }
        else
        {
            var pattern = $"{Keyspace()}:*";
            var allKeys = server.Keys(db.Database, pattern: pattern, pageSize: 1000).Take(page * size).ToArray();
            var pageKeys = allKeys.Skip((page - 1) * size).Take(size).ToArray();
            var vals = await db.StringGetAsync(pageKeys);
            var list = new List<TEntity>(pageKeys.Length);
            foreach (var v in vals)
            {
                if (v.IsNull) continue;
                var e = JsonConvert.DeserializeObject<TEntity>(v!);
                if (e is not null) list.Add(e);
            }
            return (list, allKeys.Length);
        }
    }

    private sealed class RedisBatch : IBatchSet<TEntity, TKey>
    {
        private readonly RedisRepository<TEntity, TKey> _repo;
        private readonly List<TEntity> _adds = new();
        private readonly List<TEntity> _updates = new();
        private readonly List<TKey> _deletes = new();
        private readonly List<(TKey id, Action<TEntity> mutate)> _mutations = new();

        public RedisBatch(RedisRepository<TEntity, TKey> repo) => _repo = repo;

        public IBatchSet<TEntity, TKey> Add(TEntity entity) { _adds.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Update(TEntity entity) { _updates.Add(entity); return this; }
        public IBatchSet<TEntity, TKey> Delete(TKey id) { _deletes.Add(id); return this; }
        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate) { _mutations.Add((id, mutate)); return this; }
        public IBatchSet<TEntity, TKey> Clear() { _adds.Clear(); _updates.Clear(); _deletes.Clear(); _mutations.Clear(); return this; }

        public async Task<BatchResult> Save(BatchOptions? options = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (options?.RequireAtomic == true)
                throw new NotSupportedException("Redis adapter does not support atomic batch transactions for entity operations.");
            // Apply mutations
            foreach (var (id, mutate) in _mutations)
            {
                ct.ThrowIfCancellationRequested();
                var cur = await _repo.Get(id, ct);
                if (cur is not null) { mutate(cur); _updates.Add(cur); }
            }
            var addCount = 0; var updCount = 0; var delCount = 0;
            if (_adds.Count != 0) { addCount = await _repo.UpsertMany(_adds, ct); }
            if (_updates.Count != 0) { updCount = await _repo.UpsertMany(_updates, ct); }
            if (_deletes.Count != 0) { delCount = await _repo.DeleteMany(_deletes, ct); }
            return new BatchResult(addCount, updCount, delCount);
        }
    }
}
