using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.Mongo;
internal static class MongoTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Sora.Data.Mongo");
}

/// <summary>
/// MongoDB adapter options (connection string, database, and optional collection naming).
/// </summary>
public sealed class MongoOptions
{
    [Required]
    public string ConnectionString { get; set; } = MongoConstants.DefaultLocalUri; // safe default even without configurator
    [Required]
    public string Database { get; set; } = "sora";
    public Func<Type,string>? CollectionName { get; set; }
    // Naming policy controls
    public StorageNamingStyle NamingStyle { get; set; } = StorageNamingStyle.FullNamespace;
    public string Separator { get; set; } = "."; // used when composing namespace + entity

    // Paging guardrails (acceptance criteria 0044)
    public int DefaultPageSize { get; set; } = 50; // mirrors Sora.Web default
    public int MaxPageSize { get; set; } = 200;
}

internal static class MongoConstants
{
    public const string DefaultLocalUri = "mongodb://localhost:27017";
    public const string DefaultComposeUri = "mongodb://mongodb:27017";
}

public static class MongoRegistration
{
    /// <summary>
    /// Register the Mongo adapter for service discovery; optionally configure options.
    /// </summary>
    public static IServiceCollection AddMongoAdapter(this IServiceCollection services, Action<MongoOptions>? configure = null)
    {
        services.AddOptions<MongoOptions>().ValidateDataAnnotations();
        if (configure is not null) services.Configure(configure);
    // Ensure health contributor is available even outside Sora bootstrap
    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());
        services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
        return services;
    }
}

// Self-registration so the adapter participates in discovery
/// <summary>
/// Auto-registration for Mongo adapter and health contributor during Sora initialization.
/// </summary>
// legacy initializer removed in favor of standardized auto-registrar

internal sealed class MongoOptionsConfigurator(IConfiguration config) : IConfigureOptions<MongoOptions>
{
    public void Configure(MongoOptions options)
    {
        // Bind provider-specific options using Configuration helper (ADR-0040)
        options.ConnectionString = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.ConnectionString,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);
        options.Database = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.Database,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.Database,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.AltDatabase);

        // Paging guardrails
        options.DefaultPageSize = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.DefaultPageSize,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        options.MaxPageSize = Sora.Core.Configuration.ReadFirst(
            config,
            defaultValue: options.MaxPageSize,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);

        // Resolve from ConnectionStrings:Default when present. Override placeholder/empty.
    var cs = Sora.Core.Configuration.Read(config, Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault, (string?)null);
        if (!string.IsNullOrWhiteSpace(cs))
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString) || string.Equals(options.ConnectionString.Trim(), MongoConstants.DefaultLocalUri, StringComparison.OrdinalIgnoreCase))
            {
                options.ConnectionString = cs!;
            }
        }
        // Final safety default if still unset: prefer docker compose host when containerized
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            var inContainer = Sora.Core.SoraEnv.InContainer;
            options.ConnectionString = inContainer ? MongoConstants.DefaultComposeUri : MongoConstants.DefaultLocalUri;
        }

        // Normalize: ensure mongodb scheme is present to avoid driver showing "Unspecified/host:port"
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            var v = options.ConnectionString.Trim();
            if (!v.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) &&
                !v.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
            {
                options.ConnectionString = "mongodb://" + v;
            }
        }
    }

    // Container detection uses SoraEnv static runtime snapshot per ADR-0039
}

/// <summary>
/// Health probe for Mongo connectivity and database ping.
/// </summary>
internal sealed class MongoHealthContributor(IOptions<MongoOptions> options) : IHealthContributor
{
    public string Name => "data:mongo";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var client = new MongoClient(options.Value.ConnectionString);
            var db = client.GetDatabase(options.Value.Database);
            // ping
            await db.RunCommandAsync((Command<BsonDocument>)new BsonDocument("ping", 1), cancellationToken: ct);
            return new HealthReport(Name, HealthState.Healthy, null, null, new Dictionary<string, object?>
            {
                ["database"] = options.Value.Database,
                ["connectionString"] = Sora.Core.Redaction.DeIdentify(options.Value.ConnectionString)
            });
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, HealthState.Unhealthy, ex.Message, ex);
        }
    }
}

[Sora.Data.Abstractions.ProviderPriority(20)]
public sealed class MongoAdapterFactory : IDataAdapterFactory
{
    public bool CanHandle(string provider) => string.Equals(provider, "mongo", StringComparison.OrdinalIgnoreCase) || string.Equals(provider, "mongodb", StringComparison.OrdinalIgnoreCase);

    public IDataRepository<TEntity, TKey> Create<TEntity, TKey>(IServiceProvider sp) where TEntity : class, IEntity<TKey> where TKey : notnull
    {
    var opts = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
        var resolver = sp.GetRequiredService<Sora.Data.Abstractions.Naming.IStorageNameResolver>();
        return new MongoRepository<TEntity, TKey>(opts, resolver, sp);
    }
}

/// <summary>
/// Repository implementation backed by MongoDB collections; supports LINQ predicates and bulk operations.
/// </summary>
internal sealed class MongoRepository<TEntity, TKey> :
    IDataRepository<TEntity, TKey>,
    ILinqQueryRepository<TEntity, TKey>,
    IQueryCapabilities,
    IWriteCapabilities,
    IBulkUpsert<TKey>,
    IBulkDelete<TKey>,
    Sora.Data.Abstractions.Instructions.IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private IMongoCollection<TEntity> _collection;
    private string _collectionName;
    public QueryCapabilities Capabilities => QueryCapabilities.Linq;
    public WriteCapabilities Writes => WriteCapabilities.BulkUpsert | WriteCapabilities.BulkDelete;

    private readonly Sora.Data.Abstractions.Naming.IStorageNameResolver _nameResolver;
    private readonly IServiceProvider _sp;
    private readonly Sora.Data.Abstractions.Naming.StorageNameResolver.Convention _nameConv;
    private readonly ILogger? _logger;
    private readonly int _defaultPageSize;
    private readonly int _maxPageSize;

    public MongoRepository(MongoOptions options, Sora.Data.Abstractions.Naming.IStorageNameResolver nameResolver, IServiceProvider sp)
    {
        _nameResolver = nameResolver;
        _sp = sp;
    _logger = sp.GetService<ILogger<MongoRepository<TEntity, TKey>>>();
        var client = new MongoClient(options.ConnectionString);
        var db = client.GetDatabase(options.Database);
        _nameConv = new Sora.Data.Abstractions.Naming.StorageNameResolver.Convention(options.NamingStyle, options.Separator ?? ".", Sora.Data.Abstractions.Naming.NameCasing.AsIs);
    // Initial collection name (may be set-scoped); will be recomputed per call if set changes
    _collectionName = Sora.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
    _collection = db.GetCollection<TEntity>(_collectionName);
        _defaultPageSize = options.DefaultPageSize > 0 ? options.DefaultPageSize : 50;
        _maxPageSize = options.MaxPageSize > 0 ? options.MaxPageSize : 200;
        CreateIndexesIfNeeded();
    }
    private IMongoCollection<TEntity> GetCollection()
    {
        var name = Sora.Data.Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        if (!string.Equals(name, _collectionName, StringComparison.Ordinal))
        {
            var clientField = typeof(IMongoCollection<TEntity>).GetProperty("Database")?.GetValue(_collection) as IMongoDatabase;
            // Fall back: rebuild from options if reflection fails
            if (clientField is null)
            {
                var opts = _sp.GetRequiredService<IOptions<MongoOptions>>().Value;
                var client = new MongoClient(opts.ConnectionString);
                var db = client.GetDatabase(opts.Database);
                _collection = db.GetCollection<TEntity>(name);
            }
            else
            {
                _collection = clientField.GetCollection<TEntity>(name);
            }
            _collectionName = name;
            CreateIndexesIfNeeded();
        }
        return _collection;
    }

    private void CreateIndexesIfNeeded()
    {
        try
        {
            var keys = Builders<TEntity>.IndexKeys.Ascending(x => x.Id);
            _collection.Indexes.CreateOne(new CreateIndexModel<TEntity>(keys, new CreateIndexOptions { Unique = true, Name = "_id_unique" }));
        }
        catch { /* best-effort */ }
    }

    public async Task<TEntity?> GetAsync(TKey id, CancellationToken ct = default)
    {
    using var act = MongoTelemetry.Activity.StartActivity("mongo.get");
    act?.SetTag("entity", typeof(TEntity).FullName);
    var col = GetCollection();
    var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
    var result = await col.Find(filter).FirstOrDefaultAsync(ct);
    _logger?.LogDebug("Mongo get {Entity} id={Id} found={Found}", typeof(TEntity).Name, id, result is not null);
    return result;
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        using var act = MongoTelemetry.Activity.StartActivity("mongo.query.all");
        act?.SetTag("entity", typeof(TEntity).FullName);
    // Guardrails: enforce server-side paging if possible to avoid unbounded materialization.
    var col = GetCollection();
    var find = col.Find(Builders<TEntity>.Filter.Empty);
    find = find.Limit(_defaultPageSize);
    return await find.ToListAsync(ct);
    }

    public async Task<int> CountAsync(object? query, CancellationToken ct = default)
    {
        using var act = MongoTelemetry.Activity.StartActivity("mongo.count");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var count = await GetCollection().CountDocumentsAsync(Builders<TEntity>.Filter.Empty, cancellationToken: ct);
        return (int)count;
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        using var act = MongoTelemetry.Activity.StartActivity("mongo.query.linq");
        act?.SetTag("entity", typeof(TEntity).FullName);
    var col = GetCollection();
    var find = col.Find(predicate).Limit(_defaultPageSize);
    return await find.ToListAsync(ct);
    }

    public async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken ct = default)
    {
        using var act = MongoTelemetry.Activity.StartActivity("mongo.count.linq");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var count = await GetCollection().CountDocumentsAsync(predicate, cancellationToken: ct);
        return (int)count;
    }

    public async Task<TEntity> UpsertAsync(TEntity model, CancellationToken ct = default)
    {
    using var act = MongoTelemetry.Activity.StartActivity("mongo.upsert");
    act?.SetTag("entity", typeof(TEntity).FullName);
    var col = GetCollection();
    var filter = Builders<TEntity>.Filter.Eq(x => x.Id, model.Id);
    await col.ReplaceOneAsync(filter, model, new ReplaceOptions { IsUpsert = true }, ct);
    _logger?.LogDebug("Mongo upsert {Entity} id={Id}", typeof(TEntity).Name, model.Id);
        return model;
    }

    public async Task<bool> DeleteAsync(TKey id, CancellationToken ct = default)
    {
    using var act = MongoTelemetry.Activity.StartActivity("mongo.delete");
    act?.SetTag("entity", typeof(TEntity).FullName);
    var col = GetCollection();
    var filter = Builders<TEntity>.Filter.Eq(x => x.Id, id);
    var result = await col.DeleteOneAsync(filter, ct);
    var deleted = result.DeletedCount > 0;
    _logger?.LogDebug("Mongo delete {Entity} id={Id} deleted={Deleted}", typeof(TEntity).Name, id, deleted);
    return deleted;
    }

    public async Task<int> UpsertManyAsync(IEnumerable<TEntity> models, CancellationToken ct = default)
    {
        using var act = MongoTelemetry.Activity.StartActivity("mongo.bulk.upsert");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var col = GetCollection();
        var writes = models.Select(m => new ReplaceOneModel<TEntity>(Builders<TEntity>.Filter.Eq(x => x.Id, m.Id), m) { IsUpsert = true });
        var res = await col.BulkWriteAsync(writes, cancellationToken: ct);
        var count = (int)(res.ModifiedCount + res.Upserts.Count);
        _logger?.LogInformation("Mongo bulk upsert {Entity} count={Count}", typeof(TEntity).Name, count);
        return count;
    }

    public async Task<int> DeleteManyAsync(IEnumerable<TKey> ids, CancellationToken ct = default)
    {
        using var act = MongoTelemetry.Activity.StartActivity("mongo.bulk.delete");
        act?.SetTag("entity", typeof(TEntity).FullName);
        var col = GetCollection();
        var filter = Builders<TEntity>.Filter.In(x => x.Id, ids);
        var res = await col.DeleteManyAsync(filter, ct);
        var count = (int)res.DeletedCount;
        _logger?.LogInformation("Mongo bulk delete {Entity} count={Count}", typeof(TEntity).Name, count);
        return count;
    }

    public async Task<int> DeleteAllAsync(CancellationToken ct = default)
    {
        var col = GetCollection();
        var res = await col.DeleteManyAsync(Builders<TEntity>.Filter.Empty, ct);
        return (int)res.DeletedCount;
    }

    public IBatchSet<TEntity, TKey> CreateBatch() => new MongoBatch(this);

    // Instruction execution for fast-path operations
    public async Task<TResult> ExecuteAsync<TResult>(Sora.Data.Abstractions.Instructions.Instruction instruction, CancellationToken ct = default)
    {
        using var act = MongoTelemetry.Activity.StartActivity("mongo.instruction");
        act?.SetTag("entity", typeof(TEntity).FullName);
        switch (instruction.Name)
        {
            case "data.ensureCreated":
            {
                var col = GetCollection();
                var db = GetDatabase(col);
                // Ensure collection exists (will no-op if it already exists)
                var name = _collectionName;
                try
                {
                    var existing = await db.ListCollectionNamesAsync(cancellationToken: ct);
                    var names = await existing.ToListAsync(ct);
                    if (!names.Contains(name, StringComparer.Ordinal))
                    {
                        await db.CreateCollectionAsync(name, cancellationToken: ct);
                        _logger?.LogInformation("Mongo ensureCreated created collection {Name}", name);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Mongo ensureCreated encountered an error for collection {Name}", name);
                }
                object ok = true;
                return (TResult)ok;
            }
            case "data.clear":
            {
                var col = GetCollection();
                var res = await col.DeleteManyAsync(Builders<TEntity>.Filter.Empty, ct).ConfigureAwait(false);
                object result = (int)res.DeletedCount;
                return (TResult)result;
            }
            default:
                throw new NotSupportedException($"Instruction '{instruction.Name}' not supported by Mongo adapter for {typeof(TEntity).Name}.");
        }
    }

    private static IMongoDatabase GetDatabase(IMongoCollection<TEntity> collection)
    {
        // Prefer direct property when available; fall back to reflection if needed.
        try
        {
            return collection.Database;
        }
        catch
        {
            var prop = typeof(IMongoCollection<TEntity>).GetProperty("Database");
            if (prop?.GetValue(collection) is IMongoDatabase db) return db;
            throw new InvalidOperationException("Unable to obtain Mongo database from collection.");
        }
    }

    private sealed class MongoBatch : IBatchSet<TEntity, TKey>
    {
        private readonly MongoRepository<TEntity, TKey> _repo;
        private readonly List<WriteModel<TEntity>> _ops = new();
        private readonly List<(TKey id, Action<TEntity> mutate)> _mutations = new();

        public MongoBatch(MongoRepository<TEntity, TKey> repo) => _repo = repo;

        public IBatchSet<TEntity, TKey> Add(TEntity entity)
        {
            _ops.Add(new ReplaceOneModel<TEntity>(Builders<TEntity>.Filter.Eq(x => x.Id, entity.Id), entity) { IsUpsert = true });
            return this;
        }
        public IBatchSet<TEntity, TKey> Update(TEntity entity) => Add(entity);
        public IBatchSet<TEntity, TKey> Delete(TKey id)
        {
            _ops.Add(new DeleteOneModel<TEntity>(Builders<TEntity>.Filter.Eq(x => x.Id, id)));
            return this;
        }
        public IBatchSet<TEntity, TKey> Update(TKey id, Action<TEntity> mutate)
        {
            _mutations.Add((id, mutate));
            return this;
        }
        public IBatchSet<TEntity, TKey> Clear() { _ops.Clear(); _mutations.Clear(); return this; }

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
                        _ops.Add(new ReplaceOneModel<TEntity>(Builders<TEntity>.Filter.Eq(x => x.Id, id), current) { IsUpsert = true });
                    }
                }
            }
            if (_ops.Count == 0) return new BatchResult(0, 0, 0);
            var collection = _repo.GetCollection();

            // Honor RequireAtomic when requested
            var requireAtomic = options?.RequireAtomic == true;
            if (requireAtomic)
            {
                // Attempt transactional execution. Transactions require replica set/sharded cluster.
                // If not supported by the deployment, signal NotSupported as per acceptance criteria.
                var db = GetDatabase(collection);
                var client = db.Client;
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

                    var resTx = await collection.BulkWriteAsync(session, _ops, cancellationToken: ct).ConfigureAwait(false);
                    await session.CommitTransactionAsync(ct).ConfigureAwait(false);
                    return new BatchResult((int)resTx.Upserts.Count, (int)resTx.ModifiedCount, (int)resTx.DeletedCount);
                }
                catch
                {
                    if (session is not null)
                    {
                        try { await session.AbortTransactionAsync(ct).ConfigureAwait(false); } catch { /* swallow */ }
                        session.Dispose();
                    }
                    throw;
                }
            }
            else
            {
                // Best-effort bulk write
                var res = await collection.BulkWriteAsync(_ops, cancellationToken: ct).ConfigureAwait(false);
                return new BatchResult((int)res.Upserts.Count, (int)res.ModifiedCount, (int)res.DeletedCount);
            }
        }

        private static IMongoDatabase GetDatabase(IMongoCollection<TEntity> collection)
        {
            // Prefer direct property when available; fall back to reflection if needed.
            try
            {
                return collection.Database;
            }
            catch
            {
                var prop = typeof(IMongoCollection<TEntity>).GetProperty("Database");
                if (prop?.GetValue(collection) is IMongoDatabase db) return db;
                throw new InvalidOperationException("Unable to obtain Mongo database from collection.");
            }
        }
    }
}

internal static class MongoNaming
{
    public static string ResolveCollectionName(Type entityType, MongoOptions options)
    {
        var conv = new Sora.Data.Abstractions.Naming.StorageNameResolver.Convention(
            options.NamingStyle,
            options.Separator ?? ".",
            Sora.Data.Abstractions.Naming.NameCasing.AsIs
        );
        return Sora.Data.Abstractions.Naming.StorageNameResolver.Resolve(entityType, conv);
    }
}

internal sealed class MongoNamingDefaultsProvider : Sora.Data.Abstractions.Naming.INamingDefaultsProvider
{
    public string Provider => "mongo";
    public Sora.Data.Abstractions.Naming.StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        return new Sora.Data.Abstractions.Naming.StorageNameResolver.Convention(opts.NamingStyle, opts.Separator ?? ".", Sora.Data.Abstractions.Naming.NameCasing.AsIs);
    }
    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        return opts.CollectionName;
    }
}
