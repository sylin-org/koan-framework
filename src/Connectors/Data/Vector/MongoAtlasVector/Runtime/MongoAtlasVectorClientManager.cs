using System.Collections.Concurrent;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Koan.Data.Vector.Connector.MongoAtlasVector;

internal sealed class MongoAtlasVectorClientManager(
    IConfiguration configuration,
    IOptions<MongoAtlasVectorOptions> options,
    IServiceDiscoveryCoordinator? discovery = null) : IDisposable
{
    private readonly ConcurrentDictionary<RouteKey, Lazy<Task<RouteClient>>> _clients = new();
    private readonly object _admission = new();
    private readonly MongoAtlasVectorOptions _options = options.Value;
    private int _disposed;

    internal async Task<IMongoDatabase> Database(MongoAtlasVectorRoute route, CancellationToken ct)
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
                    if (_clients.Count >= Infrastructure.Constants.Provider.MaximumRoutes)
                        throw new InvalidOperationException(
                            $"MongoAtlasVector reached the host bound of {Infrastructure.Constants.Provider.MaximumRoutes} physical routes.");
                    entry = new Lazy<Task<RouteClient>>(
                        () => Create(route),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    if (!_clients.TryAdd(key, entry))
                        throw new InvalidOperationException("MongoAtlasVector route admission lost exclusive ownership.");
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

    internal async Task Probe(MongoAtlasVectorRoute route, CancellationToken ct)
    {
        var database = await Database(route, ct).ConfigureAwait(false);
        _ = await database.RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1), cancellationToken: ct).ConfigureAwait(false);
        try
        {
            _ = await database.RunCommandAsync<BsonDocument>(
                new BsonDocument
                {
                    { "aggregate", Infrastructure.Constants.Wire.HealthCollection },
                    { "pipeline", new BsonArray { new BsonDocument("$listSearchIndexes", new BsonDocument()) } },
                    { "cursor", new BsonDocument() }
                },
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (MongoCommandException error) when (error.Code == 26)
        {
            // NamespaceNotFound still proves that Atlas Search recognized the native stage.
        }
        catch (MongoCommandException error)
        {
            throw AtlasRequired(route, error);
        }
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

    internal static InvalidOperationException AtlasRequired(MongoAtlasVectorRoute route, Exception error) => new(
        $"MongoAtlasVector source '{route.Source}' requires a MongoDB Atlas deployment with Search and Vector Search enabled; " +
        "ordinary MongoDB cannot execute this connector. Select Atlas or MongoDB Atlas Local, or choose another vector adapter.",
        error);

    private async Task<RouteClient> Create(MongoAtlasVectorRoute route)
    {
        var connectionString = await Resolve(route).ConfigureAwait(false);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        MongoClientSettings settings;
        try
        {
            settings = MongoClientSettings.FromConnectionString(connectionString);
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            throw new InvalidOperationException(
                "MongoAtlasVector requires a mongodb:// or mongodb+srv:// connection string.", error);
        }
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(_options.CommandTimeoutSeconds);
        settings.ConnectTimeout = TimeSpan.FromSeconds(_options.CommandTimeoutSeconds);
        var client = new MongoClient(settings);
        if (Volatile.Read(ref _disposed) != 0)
        {
            if (client is IDisposable disposable) disposable.Dispose();
            throw new ObjectDisposedException(nameof(MongoAtlasVectorClientManager));
        }
        return new RouteClient(client, client.GetDatabase(route.Database));
    }

    private async Task<string> Resolve(MongoAtlasVectorRoute route)
    {
        var intent = route.ConnectionString.Trim();
        if (intent.StartsWith("zen-garden://", StringComparison.OrdinalIgnoreCase) ||
            intent.StartsWith(Infrastructure.Constants.Configuration.ZenGardenPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (discovery is null) throw DiscoveryFailure(route, "Koan's service-discovery coordinator is unavailable.");
            var result = await discovery.ResolveServiceIntent(
                    Infrastructure.Constants.Provider.PairedMongo,
                    intent,
                    Context(route.Database),
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!result.IsSuccessful || string.IsNullOrWhiteSpace(result.ServiceUrl))
                throw DiscoveryFailure(route, result.ErrorMessage);
            return result.ServiceUrl;
        }

        if (!intent.Equals(Infrastructure.Constants.Configuration.Automatic, StringComparison.OrdinalIgnoreCase))
            return intent;
        if (configuration.GetValue(Infrastructure.Constants.Configuration.Keys.DisableAutoDetection, false) || discovery is null)
            throw DiscoveryFailure(route, "Automatic Mongo discovery is disabled or unavailable.");
        var automatic = await discovery.DiscoverService(
                Infrastructure.Constants.Provider.PairedMongo,
                Context(route.Database),
                CancellationToken.None)
            .ConfigureAwait(false);
        if (!automatic.IsSuccessful || string.IsNullOrWhiteSpace(automatic.ServiceUrl))
            throw DiscoveryFailure(route, automatic.ErrorMessage);
        return automatic.ServiceUrl;
    }

    private DiscoveryContext Context(string database) => new()
    {
        Configuration = configuration,
        RequireHealthValidation = true,
        Parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["database"] = database
        }
    };

    private void Remove(RouteKey key, Lazy<Task<RouteClient>> entry)
    {
        lock (_admission)
            _clients.TryRemove(new KeyValuePair<RouteKey, Lazy<Task<RouteClient>>>(key, entry));
    }

    private static InvalidOperationException DiscoveryFailure(MongoAtlasVectorRoute route, string? reason) => new(
        $"MongoAtlasVector source '{route.Source}' has no concrete Mongo placement. " +
        $"{reason ?? "No ready Mongo service was found."} Configure its ConnectionString, configure a paired Mongo source, " +
        "or enable Mongo service discovery. The resolved deployment must provide Atlas Vector Search.");

    private sealed record RouteKey(string ConnectionString, string Database);
    private sealed record RouteClient(MongoClient Client, IMongoDatabase Database);
}
