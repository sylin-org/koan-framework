using System.Net.Http;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Milvus;
using Koan.Data.VectorAdapterSurface.TestKit;

// Testcontainers deprecated the parameterless ContainerBuilder ctor (used 3x for the etcd+minio+milvus
// stack); the generic-container pattern still functions — suppress the deprecation (warnings-as-errors).
#pragma warning disable CS0618

namespace Koan.Data.VectorAdapterSurface.Milvus.Tests;

/// <summary>
/// Milvus cell of the vector matrix. Milvus 2.4 standalone wants three real services on a
/// shared network — etcd + minio + milvus — exactly like its official docker-compose.
///
/// <para>
/// The previous "embedded etcd via ETCD_USE_EMBED" pattern doesn't exist in Milvus 2.4; that
/// env var is a leftover from older Milvus versions. The 2.4 binary always dials its configured
/// etcd endpoint (default <c>localhost:2379</c>) and SIGABRT-exits at startup when it can't
/// reach one. The three-service stack matches what Milvus actually ships as production-shape.
/// </para>
///
/// <para>
/// Operational defaults: <see cref="MilvusOptions.Dimension"/> defaults to 1536 (OpenAI ada-002
/// / text-embedding-3-small size). Users with other embedding models override.
/// </para>
/// </summary>
public sealed class MilvusTestFactory : IVectorAdapterTestFactory
{
    private INetwork? _network;
    private IContainer? _etcd;
    private IContainer? _minio;
    private IContainer? _milvus;
    private ServiceProvider? _sp;
    private string? _endpoint;
    private HttpClient? _adminHttp;
    private bool _initialized;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public IServiceProvider Services => _sp ?? throw new InvalidOperationException("Factory not initialized.");
    public int EmbeddingDimension => 8;

    public bool SupportsGetEmbedding         => false; // not implemented in MilvusVectorRepository
    public bool SupportsBulkOperations       => true;
    public bool SupportsFlush                => true;  // adapter overrides: drops the collection
    public bool SupportsExportAll            => false;
    public bool SupportsIndexStats           => false; // not implemented in MilvusVectorRepository
    public bool SupportsHybridSearch         => false;
    public bool SupportsMetadataFilters      => true;  // metadata["key"] JSON-field access via MilvusFilterTranslator (live-verified)
    public bool SupportsContinuationToken    => false;
    public bool SupportsPartitionIsolation   => true;
    public bool SupportsDynamicCollections   => true;
    // Milvus 2.4 REST has no flush/compact endpoint; KNN search runs against growing segments
    // where filter-based deletes don't land until segments seal naturally. Point-lookup Query
    // sees deletes immediately, KNN Search does not. Verified across 2.4.0 and 2.4.13.
    public bool SupportsDeleteImmediatelyVisibleToSearch => false;
    public bool SupportsScoreNormalization   => true;  // cosine

    public async ValueTask InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var envEndpoint = Environment.GetEnvironmentVariable("Koan_TESTS_MILVUS")
                          ?? Environment.GetEnvironmentVariable("MILVUS_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(envEndpoint) && await Ping(envEndpoint))
        {
            _endpoint = envEndpoint;
            BuildSp();
            IsAvailable = true;
            return;
        }

        try
        {
            _network = new NetworkBuilder().Build();
            await _network.CreateAsync();

            _etcd = new ContainerBuilder()
                .WithImage("quay.io/coreos/etcd:v3.5.5")
                .WithNetwork(_network)
                .WithNetworkAliases("etcd")
                .WithEnvironment("ETCD_AUTO_COMPACTION_MODE", "revision")
                .WithEnvironment("ETCD_AUTO_COMPACTION_RETENTION", "1000")
                .WithEnvironment("ETCD_QUOTA_BACKEND_BYTES", "4294967296")
                .WithEnvironment("ETCD_SNAPSHOT_COUNT", "50000")
                .WithCommand(
                    "etcd",
                    "-advertise-client-urls=http://etcd:2379",
                    "-listen-client-urls=http://0.0.0.0:2379",
                    "--data-dir=/etcd")
                // Wait for etcd's "ready to serve client requests" log line. The earlier
                // `etcdctl endpoint health` strategy hangs on Windows hosts (Testcontainers tries
                // to run the command in the wrong context); log-message matching is portable.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("ready to serve client requests"))
                .Build();
            await _etcd.StartAsync();

            _minio = new ContainerBuilder()
                .WithImage("minio/minio:RELEASE.2023-03-20T20-16-18Z")
                .WithNetwork(_network)
                .WithNetworkAliases("minio")
                .WithEnvironment("MINIO_ACCESS_KEY", "minioadmin")
                .WithEnvironment("MINIO_SECRET_KEY", "minioadmin")
                .WithCommand("minio", "server", "/minio_data")
                // Wait strategy uses logs, not HTTP — minio's port isn't exposed to the host
                // (only milvus needs to reach it via the internal Docker network), so the
                // Testcontainers HTTP poller can't connect from the host side.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("API:"))
                .Build();
            await _minio.StartAsync();

            _milvus = new ContainerBuilder()
                // 2.4.13 is the latest 2.4.x stable; 2.4.0 has REST API quirks (notably
                // single-id delete-by-filter being silently dropped on growing segments)
                // that were fixed in later patch releases.
                .WithImage("milvusdb/milvus:v2.4.13")
                .WithNetwork(_network)
                .WithNetworkAliases("milvus")
                .WithEnvironment("ETCD_ENDPOINTS", "etcd:2379")
                .WithEnvironment("MINIO_ADDRESS", "minio:9000")
                .WithCommand("milvus", "run", "standalone")
                .WithPortBinding(19530, true)
                .WithPortBinding(9091, true)
                // "Proxy successfully started" is the last bootstrap line milvus emits when the
                // gRPC frontend at 19530 is accepting client connections. Log-based wait
                // sidesteps Windows Docker Desktop quirks where the HTTP-from-host poller
                // doesn't resolve the mapped public port reliably.
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Proxy successfully started"))
                .Build();
            await _milvus.StartAsync();
            // The wait strategy ("Proxy successfully started" log line) is already authoritative
            // — once it satisfies, milvus is accepting client connections on 19530. Skipping a
            // follow-up HTTP ping loop because Milvus doesn't expose REST /healthz on 19530
            // anyway (that path lives on the management port 9091).
            var port = _milvus.GetMappedPublicPort(19530);
            _endpoint = $"http://localhost:{port}";
            BuildSp();
            IsAvailable = true;
            return;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Milvus stack: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
    {
        _adminHttp?.Dispose();
        if (_sp is not null) await _sp.DisposeAsync();
        if (_milvus is not null) { try { await _milvus.DisposeAsync(); } catch { } }
        if (_minio is not null) { try { await _minio.DisposeAsync(); } catch { } }
        if (_etcd is not null) { try { await _etcd.DisposeAsync(); } catch { } }
        if (_network is not null) { try { await _network.DisposeAsync(); } catch { } }
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (_adminHttp is not null && _endpoint is not null)
        {
            try
            {
                // Drop every existing collection. Milvus v2 REST: POST /v2/vectordb/collections/list,
                // then POST /v2/vectordb/collections/drop for each.
                using var listResp = await _adminHttp.PostAsync("/v2/vectordb/collections/list",
                    new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), ct);
                if (listResp.IsSuccessStatusCode)
                {
                    var json = await listResp.Content.ReadAsStringAsync(ct);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("data", out var data))
                    {
                        foreach (var col in data.EnumerateArray())
                        {
                            if (col.GetString() is { } name)
                            {
                                using var _ = await _adminHttp.PostAsync("/v2/vectordb/collections/drop",
                                    new StringContent($"{{\"collectionName\":\"{name}\"}}", System.Text.Encoding.UTF8, "application/json"), ct);
                            }
                        }
                    }
                }
            }
            catch { /* best-effort */ }
        }

        if (_sp is not null) await _sp.DisposeAsync();
        BuildSp();
        Koan.Core.Hosting.App.AppHost.Current = _sp;
    }

    private void BuildSp()
    {
        if (_endpoint is null) throw new InvalidOperationException("Endpoint not resolved.");
        _adminHttp = new HttpClient { BaseAddress = new Uri(_endpoint, UriKind.Absolute) };

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddHttpClient("milvus", c => c.BaseAddress = new Uri(_endpoint));
        services.AddKoanDataVector();

        services.AddOptions<MilvusOptions>().Configure(o =>
        {
            o.ConnectionString = _endpoint;
            o.Endpoint = _endpoint;
            o.Dimension = EmbeddingDimension;
            o.AutoCreateCollection = true;
            o.Metric = "COSINE";
            o.ConsistencyLevel = "Strong"; // deterministic reads-after-writes for tests
        });
        services.AddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<IVectorAdapterFactory, MilvusVectorAdapterFactory>();

        _sp = services.BuildServiceProvider();
    }

    private static async Task<bool> Ping(string endpoint)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(endpoint, UriKind.Absolute), Timeout = TimeSpan.FromSeconds(2) };
            using var resp = await http.GetAsync("/v2/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
