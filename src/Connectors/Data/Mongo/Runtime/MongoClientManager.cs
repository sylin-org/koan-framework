using System.Collections.Concurrent;
using Koan.Data.Connector.Mongo.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Runtime;

internal sealed class MongoClientManager : IDisposable
{
    private readonly ConcurrentDictionary<RouteKey, Lazy<MongoClient>> _clients = new();
    private int _disposed;

    public IMongoDatabase Database(MongoRoute route)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var key = new RouteKey(route.ConnectionString, route.Database);
        if (_clients.Count >= Constants.Provider.MaximumRoutes && !_clients.ContainsKey(key))
            throw new InvalidOperationException(
                $"MongoDB reached the host bound of {Constants.Provider.MaximumRoutes} physical routes.");
        var client = _clients.GetOrAdd(key, static routeKey => new Lazy<MongoClient>(
            () => new MongoClient(routeKey.ConnectionString),
            LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return client.GetDatabase(route.Database);
    }

    public async Task Ping(MongoRoute route, CancellationToken ct)
    {
        var database = Database(route);
        _ = await database.RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1),
            cancellationToken: ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var client in _clients.Values)
            if (client.IsValueCreated && client.Value is IDisposable disposable) disposable.Dispose();
        _clients.Clear();
    }

    private sealed record RouteKey(string ConnectionString, string Database);
}
