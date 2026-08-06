using System.Collections.Concurrent;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Connector.Mongo.Infrastructure;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Connector.Mongo.Runtime;

internal sealed class MongoClientManager(
    IConfiguration configuration,
    IServiceDiscoveryCoordinator? discovery = null) : IDisposable
{
    private readonly ConcurrentDictionary<RouteKey, Lazy<Task<RouteClient>>> _clients = new();
    private readonly object _admission = new();
    private int _disposed;

    public async Task<IMongoDatabase> Database(MongoRoute route, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var key = new RouteKey(route.ConnectionString, route.Database);
        if (!_clients.TryGetValue(key, out var entry))
        {
            lock (_admission)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                if (!_clients.TryGetValue(key, out entry))
                {
                    if (_clients.Count >= Constants.Provider.MaximumRoutes)
                        throw new InvalidOperationException(
                            $"MongoDB reached the host bound of {Constants.Provider.MaximumRoutes} physical routes.");
                    entry = new Lazy<Task<RouteClient>>(
                        () => Create(route),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    if (!_clients.TryAdd(key, entry))
                        throw new InvalidOperationException("MongoDB route admission lost its exclusive ownership.");
                }
            }
        }

        try
        {
            return (await entry.Value.WaitAsync(ct).ConfigureAwait(false)).Database;
        }
        catch
        {
            if (entry.IsValueCreated && entry.Value is { IsFaulted: true } or { IsCanceled: true })
                Remove(key, entry);
            throw;
        }
    }

    public async Task Ping(MongoRoute route, CancellationToken ct)
    {
        var database = await Database(route, ct).ConfigureAwait(false);
        _ = await database.RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1),
            cancellationToken: ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var entry in _clients.Values)
        {
            if (!entry.IsValueCreated) continue;
            _ = entry.Value.ContinueWith(
                static completed =>
                {
                    if (completed.Status == TaskStatus.RanToCompletion &&
                        completed.Result.Client is IDisposable disposable)
                        disposable.Dispose();
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        _clients.Clear();
    }

    private async Task<RouteClient> Create(MongoRoute route)
    {
        var connectionString = await Resolve(route).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var client = new MongoClient(connectionString);
        if (Volatile.Read(ref _disposed) != 0)
        {
            if (client is IDisposable disposable) disposable.Dispose();
            throw new ObjectDisposedException(nameof(MongoClientManager));
        }
        return new RouteClient(client, client.GetDatabase(route.Database));
    }

    private async Task<string> Resolve(MongoRoute route)
    {
        var intent = route.ConnectionString.Trim();
        if (intent.StartsWith(Constants.Configuration.ZenGardenPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (discovery is null)
                throw ExplicitIntentFailure("Koan's service-discovery coordinator is unavailable.");
            var required = await discovery.ResolveServiceIntent(
                    Constants.Discovery.ServiceName,
                    intent,
                    Context(route.Database),
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!required.IsSuccessful || string.IsNullOrWhiteSpace(required.ServiceUrl))
                throw ExplicitIntentFailure(required.ErrorMessage);
            return required.ServiceUrl;
        }

        if (!string.Equals(intent, Constants.Configuration.Auto, StringComparison.OrdinalIgnoreCase))
            return intent;
        if (configuration.GetValue(Constants.Configuration.DisableAutoDetection, false) || discovery is null)
            return Constants.Discovery.LocalConnectionString;
        var automatic = await discovery.DiscoverService(
                Constants.Discovery.ServiceName,
                Context(route.Database),
                CancellationToken.None)
            .ConfigureAwait(false);
        return automatic.IsSuccessful && !string.IsNullOrWhiteSpace(automatic.ServiceUrl)
            ? automatic.ServiceUrl
            : Constants.Discovery.LocalConnectionString;
    }

    private DiscoveryContext Context(string database) => new()
    {
        Configuration = configuration,
        RequireHealthValidation = true,
        Parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [Constants.Discovery.DatabaseParameter] = database
        }
    };

    private void Remove(RouteKey key, Lazy<Task<RouteClient>> entry)
    {
        lock (_admission)
            _clients.TryRemove(new KeyValuePair<RouteKey, Lazy<Task<RouteClient>>>(key, entry));
    }

    private static InvalidOperationException ExplicitIntentFailure(string? reason) => new(
        "Mongo explicit Zen Garden intent for 'mongodb' could not be satisfied. " +
        $"{reason ?? "No ready MongoDB offering was found."} " +
        "Reference and enable Koan.ZenGarden with a ready 'mongodb' offering, choose 'auto', " +
        "or provide a native MongoDB connection string.");

    private sealed record RouteKey(string ConnectionString, string Database);
    private sealed record RouteClient(MongoClient Client, IMongoDatabase Database);
}
