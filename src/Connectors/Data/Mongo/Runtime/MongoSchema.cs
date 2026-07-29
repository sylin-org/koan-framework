using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Connector.Mongo.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Runtime;

internal sealed class MongoSchema<TEntity, TKey>(
    MongoRoute route,
    MongoClientManager clients,
    MongoEntityPlan<TEntity, TKey> entity)
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Lazy<Task>> _entries = new(StringComparer.Ordinal);

    public Task Ensure(string collection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Lazy<Task> entry;
        lock (_gate)
        {
            if (!_entries.TryGetValue(collection, out entry!))
            {
                if (_entries.Count >= Constants.Provider.MaximumCollectionsPerRepository)
                    throw new InvalidOperationException(
                        $"MongoDB reached the bounded collection-plan limit of " +
                        $"{Constants.Provider.MaximumCollectionsPerRepository} for '{typeof(TEntity).FullName}'.");
                entry = new Lazy<Task>(() => EnsureCore(collection, ct), LazyThreadSafetyMode.ExecutionAndPublication);
                _entries.Add(collection, entry);
            }
        }
        return Observe(collection, entry);
    }

    private async Task Observe(string collection, Lazy<Task> entry)
    {
        try { await entry.Value.ConfigureAwait(false); }
        catch
        {
            lock (_gate)
                if (_entries.TryGetValue(collection, out var current) && ReferenceEquals(current, entry))
                    _entries.Remove(collection);
            throw;
        }
    }

    private async Task EnsureCore(string collection, CancellationToken ct)
    {
        var database = clients.Database(route);
        var exists = await Exists(database, collection, ct).ConfigureAwait(false);
        if (!exists && route.StorageLifecycle == StorageLifecycle.External)
            throw new InvalidOperationException(
                $"External MongoDB collection '{route.Database}/{collection}' does not exist. " +
                "Create it outside Koan or select StorageLifecycle=Managed.");
        if (!exists) await database.CreateCollectionAsync(collection, cancellationToken: ct).ConfigureAwait(false);
        if (route.StorageLifecycle == StorageLifecycle.External) return;
        await EnsureIndexes(database.GetCollection<BsonDocument>(collection), ct).ConfigureAwait(false);
    }

    private async Task EnsureIndexes(IMongoCollection<BsonDocument> collection, CancellationToken ct)
    {
        var indexes = new List<CreateIndexModel<BsonDocument>>();
        if (entity.Mapping is { } mapping)
        {
            foreach (var index in mapping.Indexes.Where(static index => !index.Primary))
            {
                var keys = index.Bindings.Select(binding =>
                    Builders<BsonDocument>.IndexKeys.Ascending(MongoValues.Path(binding.PhysicalPath)));
                indexes.Add(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Combine(keys),
                    new CreateIndexOptions
                    {
                        Name = index.Name,
                        Unique = index.Unique,
                        ExpireAfter = index.Ttl ? TimeSpan.Zero : null
                    }));
            }
        }
        else
        {
            foreach (var index in IndexMetadata.GetIndexes(typeof(TEntity)).Where(static index => !index.IsPrimaryKey))
            {
                var keys = index.Properties.Select(property =>
                {
                    var path = FieldPath.Of(property.Name);
                    var resolved = FieldPathResolver.Resolve(typeof(TEntity), path);
                    return Builders<BsonDocument>.IndexKeys.Ascending(entity.Field(path, resolved, MappingConsumer.Index));
                });
                indexes.Add(new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys.Combine(keys),
                    new CreateIndexOptions
                    {
                        Name = index.Name,
                        Unique = index.Unique,
                        ExpireAfter = index.Ttl ? TimeSpan.Zero : null
                    }));
            }
        }
        if (indexes.Count != 0)
            await collection.Indexes.CreateManyAsync(indexes, cancellationToken: ct).ConfigureAwait(false);
    }

    private static async Task<bool> Exists(IMongoDatabase database, string collection, CancellationToken ct)
    {
        using var cursor = await database.ListCollectionNamesAsync(
            new ListCollectionNamesOptions
            {
                Filter = new BsonDocument("name", collection)
            },
            ct).ConfigureAwait(false);
        return await cursor.AnyAsync(ct).ConfigureAwait(false);
    }
}
