using System.Linq;
using System.Linq.Expressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions;
using StackExchange.Redis;

namespace Sora.Data.Redis;

public sealed class RedisOptions
{
    public string? ConnectionString { get; set; }
    public int Database { get; set; } = 0;
    public int DefaultPageSize { get; set; } = 1000;
    public int MaxPageSize { get; set; } = 10_000;
}

internal sealed class RedisOptionsConfigurator : IConfigureOptions<RedisOptions>
{
    private readonly IConfiguration? _cfg;
    public RedisOptionsConfigurator() { }
    public RedisOptionsConfigurator(IConfiguration cfg) { _cfg = cfg; }
    public void Configure(RedisOptions o)
    {
        var cs = Sora.Core.Configuration.ReadFirst(_cfg,
            Sora.Data.Redis.Infrastructure.Constants.Discovery.EnvRedisUrl,
            Sora.Data.Redis.Infrastructure.Constants.Discovery.EnvRedisConnectionString,
            $"{Sora.Data.Redis.Infrastructure.Constants.Configuration.Section_Data}:{Sora.Data.Redis.Infrastructure.Constants.Configuration.Keys.ConnectionString}",
            $"{Sora.Data.Redis.Infrastructure.Constants.Configuration.Section_Sources_Default}:{Sora.Data.Redis.Infrastructure.Constants.Configuration.Keys.ConnectionString}");
        if (!string.IsNullOrWhiteSpace(cs)) o.ConnectionString = cs;
        var db = Sora.Core.Configuration.ReadFirst(_cfg, o.Database,
            $"{Sora.Data.Redis.Infrastructure.Constants.Configuration.Section_Data}:{Sora.Data.Redis.Infrastructure.Constants.Configuration.Keys.Database}",
            $"{Sora.Data.Redis.Infrastructure.Constants.Configuration.Section_Sources_Default}:{Sora.Data.Redis.Infrastructure.Constants.Configuration.Keys.Database}");
        o.Database = db;
        var def = Sora.Core.Configuration.ReadFirst(_cfg, o.DefaultPageSize,
            $"{Sora.Data.Redis.Infrastructure.Constants.Configuration.Section_Data}:{Sora.Data.Redis.Infrastructure.Constants.Configuration.Keys.DefaultPageSize}",
            $"{Sora.Data.Redis.Infrastructure.Constants.Configuration.Section_Sources_Default}:{Sora.Data.Redis.Infrastructure.Constants.Configuration.Keys.DefaultPageSize}");
        if (def > 0) o.DefaultPageSize = def;
        var max = Sora.Core.Configuration.ReadFirst(_cfg, o.MaxPageSize,
            $"{Sora.Data.Redis.Infrastructure.Constants.Configuration.Section_Data}:{Sora.Data.Redis.Infrastructure.Constants.Configuration.Keys.MaxPageSize}",
            $"{Sora.Data.Redis.Infrastructure.Constants.Configuration.Section_Sources_Default}:{Sora.Data.Redis.Infrastructure.Constants.Configuration.Keys.MaxPageSize}");
        if (max > 0) o.MaxPageSize = max;
        if (o.DefaultPageSize > o.MaxPageSize) o.DefaultPageSize = o.MaxPageSize;

        if (string.IsNullOrWhiteSpace(o.ConnectionString))
        {
            // host/docker discovery pattern
            o.ConnectionString = Sora.Core.SoraEnv.InContainer ? Sora.Data.Redis.Infrastructure.Constants.Discovery.DefaultCompose : Sora.Data.Redis.Infrastructure.Constants.Discovery.DefaultLocal;
        }
    }
}

[Sora.Data.Abstractions.ProviderPriority(5)]
public sealed class RedisAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "redis", StringComparison.OrdinalIgnoreCase);
    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
    var opts = sp.GetRequiredService<IOptions<RedisOptions>>();
    var muxer = sp.GetRequiredService<IConnectionMultiplexer>();
    return new RedisRepository<TEntity, TKey>(opts, muxer, sp.GetService<ILoggerFactory>());
    }
}

internal sealed class RedisRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    IDataRepositoryWithOptions<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    ILinqQueryRepositoryWithOptions<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    Sora.Data.Abstractions.Instructions.IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IOptions<RedisOptions> _options;
    private readonly IConnectionMultiplexer _muxer;
    private readonly ILogger? _logger;

    public RedisRepository(IOptions<RedisOptions> options, IConnectionMultiplexer muxer, ILoggerFactory? lf)
    { _options = options; _muxer = muxer; _logger = lf?.CreateLogger("Sora.Data.Redis"); }

    public QueryCapabilities Capabilities => QueryCapabilities.Linq; // predicate filtering in-memory
    public WriteCapabilities Writes => default; // no native bulk

    private string Keyspace()
    {
        var sp = Sora.Core.SoraApp.Current;
        if (sp is not null)
        {
            return Sora.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(sp);
        }
        return typeof(TEntity).Name;
    }

    private IDatabase Db() => _muxer.GetDatabase(_options.Value.Database);

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = $"{Keyspace()}:{id}";
        var v = await Db().StringGetAsync(key);
        if (v.IsNullOrEmpty) return null;
        return System.Text.Json.JsonSerializer.Deserialize<TEntity>(v!);
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var pageSize = Math.Max(1, Math.Min(_options.Value.DefaultPageSize, _options.Value.MaxPageSize));
        var (items, _) = await ScanAll(page: 1, size: pageSize, ct);
        return items;
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var page = options?.Page is int p && p > 1 ? p : 1;
        var max = Math.Max(1, _options.Value.MaxPageSize);
        var size = options?.PageSize is int ps && ps > 0 ? Math.Min(ps, max) : Math.Min(_options.Value.DefaultPageSize, max);
        var (items, _) = await ScanAll(page, size, ct);
        return items;
    }

    public async Task<int> CountAsync(object? query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (_, total) = await ScanAll(page: 1, size: int.MaxValue, ct);
        return total;
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var pageSize = Math.Max(1, Math.Min(_options.Value.DefaultPageSize, _options.Value.MaxPageSize));
        var (items, _) = await ScanAll(page: 1, size: pageSize, ct);
        return items.AsQueryable().Where(predicate).ToList();
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, DataQueryOptions? options, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var page = options?.Page is int p && p > 1 ? p : 1;
        var max = Math.Max(1, _options.Value.MaxPageSize);
        var size = options?.PageSize is int ps && ps > 0 ? Math.Min(ps, max) : Math.Min(_options.Value.DefaultPageSize, max);
        var (items, _) = await ScanAll(page, size, ct);
        return items.AsQueryable().Where(predicate).ToList();
    }

    public async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Count across the full keyspace for this entity set
        var (items, _) = await ScanAll(page: 1, size: int.MaxValue, ct);
        return items.AsQueryable().Count(predicate);
    }

    public async Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = $"{Keyspace()}:{model.Id}";
        var json = System.Text.Json.JsonSerializer.Serialize(model);
        await Db().StringSetAsync(key, json);
        return model;
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = $"{Keyspace()}:{id}";
        return await Db().KeyDeleteAsync(key);
    }

    public async Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var arr = models as TEntity[] ?? models.ToArray();
        var entries = arr.Select(e => new KeyValuePair<RedisKey, RedisValue>($"{Keyspace()}:{e.Id}", System.Text.Json.JsonSerializer.Serialize(e))).ToArray();
        await Db().StringSetAsync(entries);
        return arr.Length;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var keys = ids.Select(id => (RedisKey)$"{Keyspace()}:{id}").ToArray();
        return (int)await Db().KeyDeleteAsync(keys);
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var (items, _) = await ScanAll(page: 1, size: int.MaxValue, ct);
        var keys = items.Select(e => (RedisKey)$"{Keyspace()}:{e.Id}").ToArray();
        if (keys.Length == 0) return 0;
        return (int)await Db().KeyDeleteAsync(keys);
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new RedisBatch(this);

    public Task<TResult> ExecuteAsync<TResult>(Sora.Data.Abstractions.Instructions.Instruction instruction, CancellationToken ct = default)
    {
        switch (instruction.Name)
        {
            case global::Sora.Data.DataInstructions.EnsureCreated:
                // Nothing to create for Redis; consider connection + ping
                return Task.FromResult((TResult)(object)true);
            case global::Sora.Data.DataInstructions.Clear:
                return (Task<TResult>)(object)DeleteAllAsync(ct);
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
            var iter = db.Execute("SCAN", 0, "MATCH", pattern, "COUNT", 1000);
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
                var e = System.Text.Json.JsonSerializer.Deserialize<TEntity>(v!);
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
                var e = System.Text.Json.JsonSerializer.Deserialize<TEntity>(v!);
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

        public async Task<BatchResult> SaveAsync(BatchOptions? options = null, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (options?.RequireAtomic == true)
                throw new NotSupportedException("Redis adapter does not support atomic batch transactions for entity operations.");
            // Apply mutations
            foreach (var (id, mutate) in _mutations)
            {
                ct.ThrowIfCancellationRequested();
                var cur = await _repo.GetAsync(id, ct);
                if (cur is not null) { mutate(cur); _updates.Add(cur); }
            }
            var addCount = 0; var updCount = 0; var delCount = 0;
            if (_adds.Count != 0) { addCount = await _repo.UpsertManyAsync(_adds, ct); }
            if (_updates.Count != 0) { updCount = await _repo.UpsertManyAsync(_updates, ct); }
            if (_deletes.Count != 0) { delCount = await _repo.DeleteManyAsync(_deletes, ct); }
            return new BatchResult(addCount, updCount, delCount);
        }
    }
}
