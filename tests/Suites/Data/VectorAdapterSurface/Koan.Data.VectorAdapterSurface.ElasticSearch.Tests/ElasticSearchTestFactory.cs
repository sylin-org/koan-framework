using System.Net.Http;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.ElasticSearch;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.ElasticSearch.Tests;

public sealed class ElasticSearchTestFactory : IVectorAdapterTestFactory
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

    public bool SupportsGetEmbedding         => false; // not implemented in ES vector repo
    public bool SupportsBulkOperations       => true;
    public bool SupportsFlush                => true;  // adapter overrides: DELETE /<index>
    public bool SupportsExportAll            => true;  // scroll API
    public bool SupportsHybridSearch         => false;
    public bool SupportsMetadataFilters      => true;  // F6 knn.filter + metadata.<key>.keyword mapping (live-verified)
    // ES Capabilities flag does not advertise NativeContinuation even though scroll exists; the
    // matrix mirrors what the adapter says, not what it could say.
    public bool SupportsContinuationToken    => false;
    // _ensuredIndexes is keyed by IndexName now, so one repo handles all partitions correctly.
    public bool SupportsPartitionIsolation   => true;
    public bool SupportsDynamicCollections   => true;
    public bool SupportsScoreNormalization   => false;

    public async ValueTask InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var envEndpoint = Environment.GetEnvironmentVariable("Koan_TESTS_ELASTICSEARCH")
                          ?? Environment.GetEnvironmentVariable("ELASTICSEARCH_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(envEndpoint) && await Ping(envEndpoint))
        {
            _endpoint = envEndpoint;
            BuildSp();
            IsAvailable = true;
            return;
        }

        try
        {
#pragma warning disable CS0618 // Testcontainers parameterless ContainerBuilder ctor deprecated; still functional.
            _container = new ContainerBuilder()
                .WithImage("docker.elastic.co/elasticsearch/elasticsearch:9.4.3")
                .WithEnvironment("discovery.type", "single-node")
                .WithEnvironment("xpack.security.enabled", "false")
                .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
                .WithPortBinding(9200, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(req => req.ForPath("/_cluster/health").ForPort(9200)))
                .Build();
#pragma warning restore CS0618

            await _container.StartAsync();
            var port = _container.GetMappedPublicPort(9200);
            _endpoint = $"http://localhost:{port}";

            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (await Ping(_endpoint)) { BuildSp(); IsAvailable = true; return; }
                await Task.Delay(500);
            }
            UnavailableReason = "ElasticSearch container did not respond after 20s.";
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start ES container: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async ValueTask DisposeAsync()
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
                // ES 8 rejects DELETE /_all by default (action.destructive_requires_name=true).
                // Enumerate live indexes then delete each by exact name.
                using var listResp = await _adminHttp.GetAsync("/_cat/indices?format=json&h=index", ct);
                if (listResp.IsSuccessStatusCode)
                {
                    var json = await listResp.Content.ReadAsStringAsync(ct);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    foreach (var row in doc.RootElement.EnumerateArray())
                    {
                        if (row.TryGetProperty("index", out var idxNode) && idxNode.GetString() is { } idx
                            && !idx.StartsWith(".", StringComparison.Ordinal)) // skip ES system indexes
                        {
                            using var _ = await _adminHttp.DeleteAsync($"/{Uri.EscapeDataString(idx)}", ct);
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
        services.AddHttpClient("elasticsearch", c => c.BaseAddress = new Uri(_endpoint));
        services.AddVectorAdapterTestRuntime();

        services.AddOptions<ElasticSearchOptions>().Configure(o =>
        {
            o.ConnectionString = _endpoint;
            o.Endpoint = _endpoint;
            o.Dimension = EmbeddingDimension;
            o.IndexPrefix = "koan_matrix";
            o.RefreshMode = "true"; // synchronous refresh so reads after writes are immediately visible
        });
        services.AddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<IVectorAdapterFactory, ElasticSearchVectorAdapterFactory>();

        _sp = services.BuildServiceProvider();
    }

    private static async Task<bool> Ping(string endpoint)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(endpoint, UriKind.Absolute), Timeout = TimeSpan.FromSeconds(2) };
            using var resp = await http.GetAsync("/_cluster/health");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
