using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Bson.Serialization;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Core;
using Koan.Data.Core.Optimization;
using System.Linq.Expressions;

namespace Koan.Data.Mongo;

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
    Abstractions.Instructions.IInstructionExecutor<TEntity>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private IMongoCollection<TEntity> _collection;
    private string _collectionName;
    public QueryCapabilities Capabilities => QueryCapabilities.Linq;
    public WriteCapabilities Writes => WriteCapabilities.BulkUpsert | WriteCapabilities.BulkDelete;

    private readonly IStorageNameResolver _nameResolver;
    private readonly IServiceProvider _sp;
    private readonly StorageNameResolver.Convention _nameConv;
    private readonly ILogger? _logger;
    private readonly int _defaultPageSize;
    private readonly int _maxPageSize;
    private readonly StorageOptimizationInfo _optimizationInfo;

    public MongoRepository(MongoOptions options, IStorageNameResolver nameResolver, IServiceProvider sp)
    {
        _nameResolver = nameResolver;
        _sp = sp;
        _logger = sp.GetService<ILogger<MongoRepository<TEntity, TKey>>>();
        var client = new MongoClient(options.ConnectionString);
        var db = client.GetDatabase(options.Database);
        _nameConv = new StorageNameResolver.Convention(options.NamingStyle, options.Separator ?? ".", NameCasing.AsIs);

        // Get storage optimization info from AggregateBag
        _optimizationInfo = sp.GetStorageOptimization<TEntity, TKey>();

        // BSON serialization optimization is now handled globally by MongoOptimizationAutoRegistrar during bootstrap

        // DEBUG: MediaFormat specific logging
        if (typeof(TEntity).Name == "MediaFormat")
        {
            Console.WriteLine($"[REPOSITORY-DEBUG] MongoRepository<MediaFormat> - Retrieved optimization info:");
            Console.WriteLine($"[REPOSITORY-DEBUG] MediaFormat - OptimizationType: {_optimizationInfo.OptimizationType}");
            Console.WriteLine($"[REPOSITORY-DEBUG] MediaFormat - IsOptimized: {_optimizationInfo.IsOptimized}");
            Console.WriteLine($"[REPOSITORY-DEBUG] MediaFormat - IdPropertyName: {_optimizationInfo.IdPropertyName}");
            Console.WriteLine($"[REPOSITORY-DEBUG] MediaFormat - Reason: {_optimizationInfo.Reason}");
        }

        // Initial collection name (may be set-scoped); will be recomputed per call if set changes
        _collectionName = Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
        _collection = db.GetCollection<TEntity>(_collectionName);
        _defaultPageSize = options.DefaultPageSize > 0 ? options.DefaultPageSize : 50;
        _maxPageSize = options.MaxPageSize > 0 ? options.MaxPageSize : 200;
        CreateIndexesIfNeeded();
    }
    private IMongoCollection<TEntity> GetCollection()
    {
        var name = Core.Configuration.StorageNameRegistry.GetOrCompute<TEntity, TKey>(_sp);
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
            // Best-effort: create secondary indexes based on IndexMetadata
            var indexSpecs = IndexMetadata.GetIndexes(typeof(TEntity));
            foreach (var idx in indexSpecs)
            {
                if (idx.IsPrimaryKey || idx.Properties.Count == 0) continue;
                IndexKeysDefinition<TEntity>? def = null;
                foreach (var p in idx.Properties)
                {
                    var field = Builders<TEntity>.IndexKeys.Ascending(p.Name);
                    def = def is null ? field : def.Ascending(p.Name);
                }
                if (def is null) continue;
                var name = !string.IsNullOrWhiteSpace(idx.Name) ? idx.Name! : $"ix_{string.Join("_", idx.Properties.Select(pp => pp.Name))}";
                var options = new CreateIndexOptions { Name = name, Unique = idx.Unique };
                _collection.Indexes.CreateOne(new CreateIndexModel<TEntity>(def, options));
            }
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
        // Removed verbose per-request get debug logging to reduce noise during parent aggregation lookups.
        return result;
    }

    public async Task<IReadOnlyList<TEntity>> QueryAsync(object? query, CancellationToken ct = default)
    {
        using var act = MongoTelemetry.Activity.StartActivity("mongo.query.all");
        act?.SetTag("entity", typeof(TEntity).FullName);
        // Guardrails: enforce server-side paging if possible to avoid unbounded materialization.
        var col = GetCollection();
        // DATA-0061: no-options should return the complete set (no implicit limit)
        return await col.Find(Builders<TEntity>.Filter.Empty).ToListAsync(ct);
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
        // DATA-0061: no-options should return the complete set for this predicate
        return await col.Find(predicate).ToListAsync(ct);
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

        // DEBUG: Track all upsert operations
        if (typeof(TEntity).Name == "MediaFormat")
        {
            Console.WriteLine($"[UPSERT-DEBUG] UpsertAsync called for MediaFormat with ID: {model.Id}");
        }

        var col = GetCollection();
        var filter = Builders<TEntity>.Filter.Eq(x => x.Id, model.Id);

        // BSON serialization handles optimization transparently
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

        // BSON serialization handles optimization transparently
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
    public async Task<TResult> ExecuteAsync<TResult>(Abstractions.Instructions.Instruction instruction, CancellationToken ct = default)
    {
        using var act = MongoTelemetry.Activity.StartActivity("mongo.instruction");
        act?.SetTag("entity", typeof(TEntity).FullName);
        switch (instruction.Name)
        {
            case Abstractions.Instructions.DataInstructions.EnsureCreated:
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
            case Abstractions.Instructions.DataInstructions.Clear:
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
            // BSON serialization handles optimization transparently
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
                        // BSON serialization handles optimization transparently
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
                    return new BatchResult(resTx.Upserts.Count, (int)resTx.ModifiedCount, (int)resTx.DeletedCount);
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
                return new BatchResult(res.Upserts.Count, (int)res.ModifiedCount, (int)res.DeletedCount);
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