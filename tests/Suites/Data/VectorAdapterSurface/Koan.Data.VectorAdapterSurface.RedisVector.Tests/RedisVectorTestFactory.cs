using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.RedisVector.Tests;

/// <summary>One pinned Redis Stack runtime shared by the inherited vector provider suite.</summary>
public sealed class RedisVectorTestFactory : IAsyncLifetime
{
    public const string Image =
        "redis/redis-stack-server@sha256:798ab84d9f266936b034ab11c4d04a2b8e4b441884c5aa7d17ac951eefdf742a";
    public const long ExpectedSearchModuleVersion = 21020;

    private RedisContainer? _container;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string ConnectionString { get; private set; } = string.Empty;
    public long SearchModuleVersion { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            var port = GrabFreePort();
            _container = new RedisBuilder(Image)
                .WithPortBinding(port, 6379)
                .WithEnvironment("REDIS_ARGS", "--appendonly yes --appendfsync always")
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
            ConnectionString = $"127.0.0.1:{port},abortConnect=false,allowAdmin=true,connectTimeout=5000,syncTimeout=5000";
            SearchModuleVersion = await VerifySearchModule().ConfigureAwait(false);
            await VerifyAof().ConfigureAwait(false);
            IsAvailable = true;
        }
        catch (Exception error)
        {
            UnavailableReason =
                $"Redis Stack/vector search unavailable from pinned image {Image}: {error.GetType().Name}: {error.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is null) return;
        try { await _container.DisposeAsync().ConfigureAwait(false); }
        catch { }
    }

    public async Task Reset(CancellationToken ct = default)
    {
        if (!IsAvailable) return;
        using var connection = await Connect(ct).ConfigureAwait(false);
        _ = await connection.GetDatabase().ExecuteAsync("FLUSHALL", "SYNC").WaitAsync(ct).ConfigureAwait(false);
    }

    public async Task CreateWrongShapeIndex(
        string indexName,
        int dimensions,
        string embeddingField,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingField);
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions));

        using var connection = await Connect(ct).ConfigureAwait(false);
        var prefix = PointPrefix(indexName);
        _ = await connection.GetDatabase().ExecuteAsync(
            "FT.CREATE",
            indexName,
            "ON", "HASH",
            "PREFIX", 1, prefix,
            "SCHEMA",
            "__koan_scope", "TAG",
            "__koan_key", "TAG",
            "__koan_present", "TAG", "SEPARATOR", "|",
            "__koan_scalar", "TAG", "SEPARATOR", "|",
            "__koan_elements", "TAG", "SEPARATOR", "|",
            "__koan_unordered", "TAG", "SEPARATOR", "|",
            embeddingField, "VECTOR", "FLAT", 6,
            "TYPE", "FLOAT32",
            "DIM", dimensions,
            "DISTANCE_METRIC", "COSINE").WaitAsync(ct).ConfigureAwait(false);
    }

    public async Task AssertFlatIndex(
        string indexName,
        int dimensions,
        string metric,
        CancellationToken ct = default)
    {
        using var connection = await Connect(ct).ConfigureAwait(false);
        var info = await connection.GetDatabase()
            .ExecuteAsync("FT.INFO", indexName)
            .WaitAsync(ct)
            .ConfigureAwait(false);
        var tokens = Flatten(info).ToArray();
        Assert.Contains(tokens, token => string.Equals(token, "FLAT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(tokens, token => string.Equals(token, dimensions.ToString(), StringComparison.Ordinal));
        Assert.Contains(tokens, token => string.Equals(token, metric, StringComparison.OrdinalIgnoreCase));
    }

    public async Task Restart(IConnectionMultiplexer managedConnection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(managedConnection);
        if (_container is null) throw new InvalidOperationException(UnavailableReason);
        await _container.StopAsync(ct).ConfigureAwait(false);
        await _container.StartAsync(ct).ConfigureAwait(false);

        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readiness.CancelAfter(TimeSpan.FromSeconds(60));
        Exception? last = null;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                SearchModuleVersion = await VerifySearchModule(readiness.Token).ConfigureAwait(false);
                await VerifyAof(readiness.Token).ConfigureAwait(false);
                await managedConnection.GetDatabase()
                    .PingAsync()
                    .WaitAsync(readiness.Token)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception error) when (!readiness.IsCancellationRequested)
            {
                last = error;
                await Task.Delay(100, readiness.Token).ConfigureAwait(false);
            }
        }
        throw new TimeoutException("Redis Stack did not become ready after restart.", last);
    }

    public static string PointPrefix(string indexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(indexName);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(indexName)))
            .ToLowerInvariant()[..24];
        return $"koan:v:{{{hash}}}:point:";
    }

    private async Task<long> VerifySearchModule(CancellationToken ct = default)
    {
        using var connection = await Connect(ct).ConfigureAwait(false);
        var database = connection.GetDatabase();
        _ = await database.ExecuteAsync("FT._LIST").WaitAsync(ct).ConfigureAwait(false);
        var result = await database.ExecuteAsync("MODULE", "LIST").WaitAsync(ct).ConfigureAwait(false);
        foreach (var moduleResult in (RedisResult[]?)result ?? [])
        {
            var module = (RedisResult[]?)moduleResult ?? [];
            string? name = null;
            long version = 0;
            for (var index = 0; index + 1 < module.Length; index += 2)
            {
                var key = module[index].ToString();
                if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                    name = module[index + 1].ToString();
                if (string.Equals(key, "ver", StringComparison.OrdinalIgnoreCase))
                    _ = long.TryParse(module[index + 1].ToString(), out version);
            }
            if (string.Equals(name, "search", StringComparison.OrdinalIgnoreCase))
            {
                if (version != ExpectedSearchModuleVersion)
                    throw new InvalidOperationException(
                        $"Pinned Redis Stack image reported Search module {version}, expected {ExpectedSearchModuleVersion}.");
                return version;
            }
        }
        throw new InvalidOperationException(
            "The pinned Redis runtime answered FT._LIST but MODULE LIST did not report the Search module.");
    }

    private async Task VerifyAof(CancellationToken ct = default)
    {
        using var connection = await Connect(ct).ConfigureAwait(false);
        var result = await connection.GetDatabase()
            .ExecuteAsync("CONFIG", "GET", "appendonly")
            .WaitAsync(ct)
            .ConfigureAwait(false);
        var values = (RedisResult[]?)result ?? [];
        if (values.Length != 2 ||
            !string.Equals(values[0].ToString(), "appendonly", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(values[1].ToString(), "yes", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The pinned Redis runtime did not enable AOF; restart durability cannot be claimed.");
    }

    private async Task<ConnectionMultiplexer> Connect(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var connection = await ConnectionMultiplexer.ConnectAsync(ConnectionString).WaitAsync(ct).ConfigureAwait(false);
        await connection.GetDatabase().PingAsync().WaitAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private static IEnumerable<string> Flatten(RedisResult result)
    {
        RedisResult[]? items = null;
        try { items = (RedisResult[]?)result; }
        catch (InvalidCastException) { }
        if (items is not null)
        {
            foreach (var item in items)
                foreach (var token in Flatten(item))
                    yield return token;
            yield break;
        }
        var value = result.ToString();
        if (!string.IsNullOrWhiteSpace(value)) yield return value;
    }

    private static int GrabFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
