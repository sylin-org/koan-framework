using System.Net.Http;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Milvus;
using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.Milvus.Tests;

/// <summary>
/// Milvus cell of the vector matrix. Milvus 2.4 standalone via embedded etcd
/// (<c>milvusdb/milvus:v2.4.0</c>, etcd in-process via ETCD_USE_EMBED=true). First Milvus adapter
/// coverage in the repo — the adapter had zero tests before this.
/// </summary>
public sealed class MilvusTestFactory : IVectorAdapterTestFactory
{
    private IContainer? _container;
    private ServiceProvider? _sp;
    private string? _endpoint;
    private HttpClient? _adminHttp;
    private bool _initialized;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public IServiceProvider Services => _sp ?? throw new InvalidOperationException("Factory not initialized.");
    public int EmbeddingDimension => 8;

    public bool SupportsGetEmbedding         => false; // not implemented in Milvus vector repo
    public bool SupportsBulkOperations       => true;
    public bool SupportsFlush                => true;  // delete with expr="true"
    public bool SupportsExportAll            => false; // no scroll/export override
    public bool SupportsHybridSearch         => false;
    public bool SupportsMetadataFilters      => true;  // expr-string filters
    public bool SupportsContinuationToken    => false;
    public bool SupportsPartitionIsolation   => true;
    public bool SupportsDynamicCollections   => true;  // AutoCreateCollection=true
    public bool SupportsScoreNormalization   => true;  // cosine

    public async Task InitializeAsync()
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
            _container = new ContainerBuilder()
                .WithImage("milvusdb/milvus:v2.4.0")
                .WithEnvironment("ETCD_USE_EMBED", "true")
                .WithEnvironment("ETCD_DATA_DIR", "/var/lib/milvus/etcd")
                .WithEnvironment("ETCD_CONFIG_PATH", "/milvus/configs/embedEtcd.yaml")
                .WithEnvironment("COMMON_STORAGETYPE", "local")
                .WithCommand("milvus", "run", "standalone")
                .WithPortBinding(19530, true)
                .WithPortBinding(9091, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(req => req.ForPath("/healthz").ForPort(9091)))
                .Build();

            await _container.StartAsync();
            var port = _container.GetMappedPublicPort(19530);
            _endpoint = $"http://localhost:{port}";

            for (var attempt = 0; attempt < 60; attempt++)
            {
                if (await Ping(_endpoint)) { BuildSp(); IsAvailable = true; return; }
                await Task.Delay(1000);
            }
            UnavailableReason = "Milvus container did not respond after 60s.";
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Milvus container: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        _adminHttp?.Dispose();
        if (_sp is not null) await _sp.DisposeAsync();
        if (_container is not null) { try { await _container.DisposeAsync(); } catch { } }
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
