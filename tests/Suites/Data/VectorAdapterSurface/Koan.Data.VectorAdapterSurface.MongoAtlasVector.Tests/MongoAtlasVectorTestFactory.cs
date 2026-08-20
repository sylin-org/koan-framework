using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.MongoAtlasVector.Tests;

/// <summary>One pinned Atlas Local runtime shared by the inherited provider suite.</summary>
public sealed class MongoAtlasVectorTestFactory : IAsyncLifetime
{
    private const string VectorDatabaseBase = "KoanVectors";
    private const string RecordDatabaseBase = "KoanRecords";
    internal const string Image =
        "mongodb/mongodb-atlas-local@sha256:3597ce32156af585890ddb4b08d0484f33d596d7ae9140a62199872185d91c41";

    private IContainer? _container;
    private MongoClient? _client;
    private int _testRun;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;
    internal string VectorDatabase { get; private set; } = VectorDatabaseBase;
    internal string RecordDatabase { get; private set; } = RecordDatabaseBase;

    public async ValueTask InitializeAsync()
    {
        try
        {
            var port = GrabFreePort();
            _container = new ContainerBuilder(Image)
                .WithPortBinding(port, 27017)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilContainerIsHealthy(
                    5,
                    wait => wait.WithTimeout(TimeSpan.FromMinutes(5))))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            var mapped = _container.GetMappedPublicPort(27017);
            if (mapped != port)
                throw new InvalidOperationException(
                    $"Atlas Local mapped port changed from requested {port} to {mapped}.");
            ConnectionString = Connection(port);
            _client = new MongoClient(ConnectionString);
            await Ping().ConfigureAwait(false);
            IsAvailable = true;
        }
        catch (Exception error)
        {
            UnavailableReason = $"Atlas Local/Docker unavailable: {error.GetType().Name}: {error.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is IDisposable disposable) disposable.Dispose();
        if (_container is null) return;
        try { await _container.DisposeAsync().ConfigureAwait(false); }
        catch { }
    }

    public async Task Reset(CancellationToken ct = default)
    {
        if (!IsAvailable) return;
        ct.ThrowIfCancellationRequested();
        var run = Interlocked.Increment(ref _testRun).ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        VectorDatabase = $"{VectorDatabaseBase}_{run}";
        RecordDatabase = $"{RecordDatabaseBase}_{run}";
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public IMongoDatabase Database(string? name = null) => Client().GetDatabase(name ?? VectorDatabase);

    public async Task<BsonDocument?> SearchIndex(
        string collection,
        string index,
        CancellationToken ct = default)
    {
        var cursor = await Database().GetCollection<BsonDocument>(collection)
            .SearchIndexes.ListAsync(index, cancellationToken: ct)
            .ConfigureAwait(false);
        return await cursor.FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    public async Task CreateWrongShapeIndex(
        string collection,
        string index,
        int dimensions,
        string similarity,
        CancellationToken ct = default)
    {
        await Database().CreateCollectionAsync(collection, cancellationToken: ct).ConfigureAwait(false);
        var definition = SearchDefinition(dimensions, similarity);
        _ = await Database().GetCollection<BsonDocument>(collection).SearchIndexes.CreateOneAsync(
                new CreateSearchIndexModel(index, SearchIndexType.Search, definition),
                ct)
            .ConfigureAwait(false);
        _ = await WaitForSearchIndex(collection, index, ct).ConfigureAwait(false);
    }

    public async Task<BsonDocument> WaitForSearchIndex(
        string collection,
        string index,
        CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        Exception? last = null;
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var current = await SearchIndex(collection, index, timeout.Token).ConfigureAwait(false);
                if (current is not null &&
                    current.GetValue("queryable", false).AsBoolean &&
                    string.Equals(current.GetValue("status", "").AsString, "READY", StringComparison.OrdinalIgnoreCase))
                    return current;
            }
            catch (Exception error) when (!timeout.IsCancellationRequested)
            {
                last = error;
            }

            await Task.Delay(50, timeout.Token).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Atlas search index '{index}' on '{VectorDatabase}.{collection}' did not become queryable.", last);
    }

    /// <summary>
    /// Waits for something Atlas makes true eventually rather than immediately.
    ///
    /// <para>A search index reports READY once it exists and accepts queries, which is not the same as having
    /// finished indexing the documents already in the collection. After a restart the two diverge: a point is
    /// readable by id straight away and absent from search results for a while longer. Asserting the second
    /// against the first is a race, and it fails on whichever machine is slower that day.</para>
    ///
    /// <para>Waiting does not soften the claim. A point that is genuinely lost never appears, and the timeout
    /// then names what never became true.</para>
    /// </summary>
    public static async Task WaitUntil(
        Func<Task<bool>> satisfied,
        string description,
        CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        while (!timeout.IsCancellationRequested)
        {
            if (await satisfied().ConfigureAwait(false)) return;
            try
            {
                await Task.Delay(100, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (await satisfied().ConfigureAwait(false)) return;
        throw new TimeoutException($"Waited for {description}, which never became true.");
    }

    public async Task EnableProfiling(bool enabled, CancellationToken ct = default)
    {
        _ = await Database().RunCommandAsync<BsonDocument>(
                new BsonDocument
                {
                    ["profile"] = enabled ? 2 : 0,
                    ["slowms"] = 0
                },
                cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task<BsonDocument?> LastVectorSearchCommand(string collection, CancellationToken ct = default)
    {
        var profile = Database().GetCollection<BsonDocument>("system.profile");
        var recent = await profile.Find(Builders<BsonDocument>.Filter.Eq("command.aggregate", collection))
            .Sort(Builders<BsonDocument>.Sort.Descending("$natural"))
            .Limit(20)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return recent.FirstOrDefault(static item =>
            item.TryGetValue("command", out var commandValue) && commandValue.IsBsonDocument &&
            commandValue.AsBsonDocument.TryGetValue("pipeline", out var pipelineValue) && pipelineValue.IsBsonArray &&
            pipelineValue.AsBsonArray.Count > 0 && pipelineValue.AsBsonArray[0].IsBsonDocument &&
            pipelineValue.AsBsonArray[0].AsBsonDocument.TryGetValue("$search", out var searchValue) &&
            searchValue.IsBsonDocument && searchValue.AsBsonDocument.Contains("vectorSearch"));
    }

    public async Task Restart(CancellationToken ct = default)
    {
        if (_container is null) throw new InvalidOperationException(UnavailableReason);
        var endpoint = ConnectionString;
        await _container.StopAsync(ct).ConfigureAwait(false);
        await _container.StartAsync(ct).ConfigureAwait(false);
        var restarted = Connection(_container.GetMappedPublicPort(27017));
        if (!string.Equals(endpoint, restarted, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Atlas Local restart changed its endpoint from '{endpoint}' to '{restarted}'.");
        await Ping(ct).ConfigureAwait(false);
    }

    private async Task Ping(CancellationToken ct = default)
    {
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readiness.CancelAfter(TimeSpan.FromMinutes(2));
        Exception? last = null;
        while (!readiness.IsCancellationRequested)
        {
            try
            {
                _ = await Database().RunCommandAsync<BsonDocument>(
                        new BsonDocument("ping", 1),
                        cancellationToken: readiness.Token)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (!readiness.IsCancellationRequested)
            {
                last = error;
                await Task.Delay(100, readiness.Token).ConfigureAwait(false);
            }
        }

        throw new TimeoutException("Atlas Local did not become ready.", last);
    }

    private MongoClient Client() => _client
        ?? throw new InvalidOperationException(UnavailableReason ?? "Atlas Local is not initialized.");

    private static BsonDocument SearchDefinition(int dimensions, string similarity) => new()
    {
        ["analyzer"] = "lucene.keyword",
        ["searchAnalyzer"] = "lucene.keyword",
        ["mappings"] = new BsonDocument
        {
            ["dynamic"] = true,
            ["fields"] = new BsonDocument
            {
                ["__koan_embedding"] = new BsonDocument
                {
                    ["type"] = "vector",
                    ["numDimensions"] = dimensions,
                    ["similarity"] = similarity
                }
            }
        }
    };

    private static string Connection(int port) =>
        $"mongodb://localhost:{port}/?directConnection=true&serverSelectionTimeoutMS=5000";

    private static int GrabFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
