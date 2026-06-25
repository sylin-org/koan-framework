using Koan.Core.Capabilities;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.KeyValue;
using StackExchange.Redis;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Koan.Data.Connector.Redis;

/// <summary>
/// Redis key/value adapter, built on the <see cref="KeyValueStore{TEntity,TKey}"/> family base (ARCH-0103 §9, the
/// JSON-text family). It inherits all three AODB modes: <b>Shared</b> (the managed discriminator is injected into /
/// extracted from the stored JSON value via the shared <see cref="ManagedFieldJsonInjector"/> — the same write-stamp the
/// relational trio and the Json adapter use — and the base's hybrid evaluator filters on it), <b>Container</b> (a
/// distinct keyspace per ambient partition, already encoded in the key prefix by <c>AdapterNaming</c>), and
/// <b>Database</b> (a distinct Redis logical database per routed source, resolved by <see cref="RedisAdapterFactory"/>).
///
/// <para>The backend keeps its Redis-native enrichments: native key TTL — a single-property <c>[Index(Ttl = true)]</c>
/// timestamp expires its key via an atomic <c>SET … PX</c> (<see cref="DataCaps.Retention"/>.TtlIndex, mirroring Mongo's
/// <c>expireAfterSeconds = 0</c>; DATA-0101) — and non-blocking fast remove (<c>UNLINK</c>). Like the in-memory
/// reference it scans the keyspace and evaluates the filter in memory, so it declares <see cref="FilterSupport.Full"/>
/// as operator-correctness (not pushdown efficiency).</para>
/// </summary>
internal sealed class RedisRepository<TEntity, TKey> : KeyValueStore<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IConnectionMultiplexer _muxer;
    private readonly int _database;

    public RedisRepository(IConnectionMultiplexer muxer, int database)
    {
        _muxer = muxer;
        _database = database;
    }

    // Database mode: the routed source selects the Redis logical database (a distinct physical keyspace on the shared
    // connection). Default source ⇒ the configured base index (0 unless overridden) ⇒ byte-identical key layout.
    private IDatabase Db() => _muxer.GetDatabase(_database);

    // Container mode: AdapterNaming composes the partition into the keyspace (e.g. "widgets#alpha"), so a per-partition
    // keyspace is a distinct physical key set — no extra work here.
    private string Keyspace()
    {
        var sp = Koan.Core.Hosting.App.AppHost.Current;
        if (sp is not null)
            return Core.Configuration.AdapterNaming.GetOrCompute<TEntity, TKey>(sp);
        return StorageNameResolver.Resolve(typeof(TEntity),
            new StorageNameResolver.Convention(StorageNamingStyle.EntityType, "_", NameCasing.AsIs));
    }

    private RedisKey KeyOf(TKey id) => $"{Keyspace()}:{id}";

    // ==================== Backend primitives ====================

    protected override async Task<KvRecord<TEntity>?> ReadAsync(TKey id, CancellationToken ct)
    {
        var v = await Db().StringGetAsync(KeyOf(id)).ConfigureAwait(false);
        return v.IsNullOrEmpty ? null : Deserialize(v!);
    }

    protected override async Task<IReadOnlyList<KvRecord<TEntity>>> ScanAsync(CancellationToken ct)
    {
        var db = Db();
        var pattern = $"{Keyspace()}:*";
        var keys = ScanKeys(db, pattern);
        if (keys.Length == 0) return [];

        var values = await db.StringGetAsync(keys).ConfigureAwait(false);
        var list = new List<KvRecord<TEntity>>(values.Length);
        foreach (var v in values)
            if (!v.IsNullOrEmpty) list.Add(Deserialize(v!));
        return list;
    }

    protected override async Task WriteAsync(TKey id, KvRecord<TEntity> record, CancellationToken ct)
        => await WriteWithTtlAsync(Db(), KeyOf(id), Serialize(record), record.Entity).ConfigureAwait(false);

    protected override async Task<bool> RemoveAsync(TKey id, CancellationToken ct)
        => await Db().KeyDeleteAsync(KeyOf(id)).ConfigureAwait(false);

    // Bulk write: one atomic MSET for non-TTL types (the common case); per-key SET PX when the type carries a TTL index
    // (MSET cannot express per-key expiry). Restores the native fast path the base's per-row default would lose.
    protected override async Task WriteManyAsync(IReadOnlyList<KvRecord<TEntity>> records, CancellationToken ct)
    {
        if (records.Count == 0) return;
        var db = Db();
        if (TtlProperty is null)
        {
            var entries = records
                .Select(r => new KeyValuePair<RedisKey, RedisValue>(KeyOf(r.Entity.Id), Serialize(r)))
                .ToArray();
            await db.StringSetAsync(entries).ConfigureAwait(false);   // MSET
            return;
        }
        foreach (var r in records)
            await WriteWithTtlAsync(db, KeyOf(r.Entity.Id), Serialize(r), r.Entity).ConfigureAwait(false);
    }

    // Bulk remove: one pipelined DEL over all keys.
    protected override async Task<int> RemoveManyAsync(IReadOnlyList<TKey> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return 0;
        var keys = ids.Select(KeyOf).ToArray();
        return (int)await Db().KeyDeleteAsync(keys).ConfigureAwait(false);
    }

    protected override async Task<int> ClearAsync(CancellationToken ct)
    {
        var db = Db();
        var keys = ScanKeys(db, $"{Keyspace()}:*");
        if (keys.Length == 0) return 0;
        // FastRemove: UNLINK reclaims the keys on a background thread (non-blocking, Redis 4.0+).
        var count = 0L;
        foreach (var key in keys)
            count += (long)await db.ExecuteAsync("UNLINK", key).ConfigureAwait(false);
        return (int)count;
    }

    // The native enrichments the family base does not own.
    protected override void DescribeBackend(ICapabilities caps) => caps
        .Add(DataCaps.Write.FastRemove)
        .Add(DataCaps.Retention.TtlIndex);   // native key TTL via [Index(Ttl = true)] → SET PX (DATA-0101)

    // ==================== Serialization (the JSON-text envelope through the shared injector) ====================

    private static string Serialize(KvRecord<TEntity> record)
    {
        var jo = JObject.FromObject(record.Entity);
        ManagedFieldJsonInjector.InjectManaged(jo, record.Managed);   // off/host-context ⇒ no key added ⇒ byte-identical
        return jo.ToString(Newtonsoft.Json.Formatting.None);
    }

    private static KvRecord<TEntity> Deserialize(string json)
    {
        var jo = JObject.Parse(json);
        var managed = ManagedFieldJsonInjector.ExtractManaged(jo, typeof(TEntity));   // null off-axis
        var entity = jo.ToObject<TEntity>()!;
        return new KvRecord<TEntity>(entity, managed);
    }

    // ==================== Redis-native key TTL (DATA-0101) ====================

    private static readonly PropertyInfo? TtlProperty = ResolveTtlProperty();

    private static PropertyInfo? ResolveTtlProperty()
    {
        foreach (var idx in Koan.Data.Core.IndexMetadata.GetIndexes(typeof(TEntity)))
            if (idx.Ttl && idx.Properties.Count == 1)
                return idx.Properties[0];
        return null;
    }

    private static DateTime? ReadExpiry(TEntity model)
        => TtlProperty!.GetValue(model) switch
        {
            DateTimeOffset dto => dto.UtcDateTime,
            DateTime dt => dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime(),
            _ => null
        };

    // Atomic single-key write honoring TTL. `SET … PX` is one round-trip — no SET-then-EXPIRE crash-leak window. A
    // future [Index(Ttl)] instant becomes the key's expiry; a past instant removes the key (already expired); a
    // null/absent value (or a non-TTL type) writes a persistent key, and SET clears any prior TTL (DATA-0101).
    private static async Task WriteWithTtlAsync(IDatabase db, RedisKey key, RedisValue json, TEntity model)
    {
        if (TtlProperty is null || ReadExpiry(model) is not { } expireAt)
        {
            await db.StringSetAsync(key, json).ConfigureAwait(false);
            return;
        }

        var ttl = expireAt - DateTime.UtcNow;
        if (ttl > TimeSpan.Zero)
            await db.StringSetAsync(key, json, ttl).ConfigureAwait(false);  // atomic SET PX
        else
            await db.KeyDeleteAsync(key).ConfigureAwait(false);             // already expired → no live key
    }

    // ==================== Keyspace scan ====================

    private RedisKey[] ScanKeys(IDatabase db, string pattern)
    {
        var server = _muxer.GetEndPoints().Select(ep => _muxer.GetServer(ep)).FirstOrDefault(s => s.IsConnected);
        if (server is not null)
            return server.Keys(db.Database, pattern: pattern, pageSize: 1000).ToArray();

        // Fallback when no server handle is available (e.g. some managed clouds): best-effort across known servers.
        return _muxer.GetServers().SelectMany(s => s.Keys(db.Database, pattern: pattern)).ToArray();
    }
}
