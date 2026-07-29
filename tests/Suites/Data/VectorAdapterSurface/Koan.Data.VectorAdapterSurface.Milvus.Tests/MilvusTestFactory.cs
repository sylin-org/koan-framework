using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Milvus.Tests;

/// <summary>One pinned live Milvus topology shared by the executable DAC-58 ledger.</summary>
public sealed class MilvusTestFactory : IAsyncLifetime
{
    private INetwork? _network;
    private IContainer? _etcd;
    private IContainer? _minio;
    private IContainer? _milvus;
    private HttpClient? _http;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public string Endpoint { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        try
        {
            _network = new NetworkBuilder().Build();
            await _network.CreateAsync().ConfigureAwait(false);

            _etcd = new ContainerBuilder("quay.io/coreos/etcd:v3.5.25")
                .WithNetwork(_network)
                .WithNetworkAliases("etcd")
                .WithEnvironment("ETCD_AUTO_COMPACTION_MODE", "revision")
                .WithEnvironment("ETCD_AUTO_COMPACTION_RETENTION", "1000")
                .WithEnvironment("ETCD_QUOTA_BACKEND_BYTES", "4294967296")
                .WithEnvironment("ETCD_SNAPSHOT_COUNT", "50000")
                .WithCommand("etcd",
                    "-advertise-client-urls=http://etcd:2379",
                    "-listen-client-urls=http://0.0.0.0:2379",
                    "--data-dir=/etcd")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("ready to serve client requests"))
                .Build();
            await _etcd.StartAsync().ConfigureAwait(false);

            _minio = new ContainerBuilder("minio/minio:RELEASE.2024-12-18T13-15-44Z")
                .WithNetwork(_network)
                .WithNetworkAliases("minio")
                .WithEnvironment("MINIO_ACCESS_KEY", "minioadmin")
                .WithEnvironment("MINIO_SECRET_KEY", "minioadmin")
                .WithCommand("minio", "server", "/minio_data")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("API:"))
                .Build();
            await _minio.StartAsync().ConfigureAwait(false);

            var hostPort = GrabFreePort();
            _milvus = new ContainerBuilder("milvusdb/milvus:v2.6.20")
                .WithNetwork(_network)
                .WithNetworkAliases("milvus")
                .WithEnvironment("ETCD_ENDPOINTS", "etcd:2379")
                .WithEnvironment("MINIO_ADDRESS", "minio:9000")
                .WithCommand("milvus", "run", "standalone")
                .WithPortBinding(hostPort, 19530)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Proxy successfully started"))
                .Build();
            await _milvus.StartAsync().ConfigureAwait(false);
            Endpoint = $"http://localhost:{hostPort}";
            _http = Client(Endpoint);
            await AwaitReady().ConfigureAwait(false);
            IsAvailable = true;
        }
        catch (Exception error)
        {
            UnavailableReason = $"Milvus/Docker unavailable: {error.GetType().Name}: {error.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        if (_milvus is not null) { try { await _milvus.DisposeAsync().ConfigureAwait(false); } catch { } }
        if (_minio is not null) { try { await _minio.DisposeAsync().ConfigureAwait(false); } catch { } }
        if (_etcd is not null) { try { await _etcd.DisposeAsync().ConfigureAwait(false); } catch { } }
        if (_network is not null) { try { await _network.DisposeAsync().ConfigureAwait(false); } catch { } }
    }

    public async Task Reset(CancellationToken ct = default)
    {
        if (!IsAvailable || _http is null) return;
        using var listed = await Post("/v2/vectordb/collections/list", new { dbName = "default" }, ct)
            .ConfigureAwait(false);
        if (!listed.RootElement.TryGetProperty("data", out var collections) ||
            collections.ValueKind != JsonValueKind.Array) return;
        foreach (var item in collections.EnumerateArray())
        {
            var name = item.GetString();
            if (name is null) continue;
            using var dropped = await Post("/v2/vectordb/collections/drop",
                new { dbName = "default", collectionName = name }, ct).ConfigureAwait(false);
        }
    }

    public async Task PutCollection(string name, CancellationToken ct = default)
    {
        using var created = await Post("/v2/vectordb/collections/create", new
        {
            dbName = "default",
            collectionName = name,
            dimension = 8,
            metricType = "COSINE"
        }, ct).ConfigureAwait(false);
    }

    public async Task Restart(CancellationToken ct = default)
    {
        if (_milvus is null) throw new InvalidOperationException(UnavailableReason);
        await _milvus.StopAsync(ct).ConfigureAwait(false);
        await _milvus.StartAsync(ct).ConfigureAwait(false);
        _http?.Dispose();
        _http = Client(Endpoint);
        await AwaitReady(ct).ConfigureAwait(false);
    }

    private async Task<JsonDocument> Post(string path, object body, CancellationToken ct)
    {
        if (_http is null) throw new InvalidOperationException(UnavailableReason);
        using var response = await _http.PostAsync(path,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (document.RootElement.TryGetProperty("code", out var code) && code.GetInt64() is not 0 and not 200)
        {
            document.Dispose();
            throw new InvalidOperationException("Milvus fixture request was rejected.");
        }
        return document;
    }

    private async Task AwaitReady(CancellationToken ct = default)
    {
        if (_http is null) throw new InvalidOperationException(UnavailableReason);
        using var readiness = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readiness.CancelAfter(TimeSpan.FromSeconds(60));
        for (var attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                using var response = await _http.PostAsync(
                    "/v2/vectordb/collections/list",
                    new StringContent("{\"dbName\":\"default\"}", Encoding.UTF8, "application/json"),
                    readiness.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(
                        await response.Content.ReadAsStringAsync(readiness.Token).ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("code", out var code) && code.GetInt64() is 0 or 200) return;
                }
            }
            catch when (!readiness.IsCancellationRequested) { }
            await Task.Delay(250, readiness.Token).ConfigureAwait(false);
        }
        throw new TimeoutException("Milvus did not become ready.");
    }

    private static HttpClient Client(string endpoint) => new()
    {
        BaseAddress = new Uri(endpoint),
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static int GrabFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
