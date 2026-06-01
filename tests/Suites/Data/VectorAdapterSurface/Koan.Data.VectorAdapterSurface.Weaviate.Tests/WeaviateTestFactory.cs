using System.Net.Http;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Weaviate;
using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.Weaviate.Tests;

/// <summary>
/// Weaviate cell of the vector matrix. Container image and env match
/// <see cref="WeaviateVectorAdapterFactory"/>'s KoanService attribute, with hybrid search
/// (Alpha + SearchText) advertised since Weaviate is the only adapter that supports it.
/// </summary>
public sealed class WeaviateTestFactory : IVectorAdapterTestFactory
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

    public bool SupportsGetEmbedding         => true;
    public bool SupportsBulkOperations       => true;
    public bool SupportsFlush                => true;
    public bool SupportsExportAll            => true;
    public bool SupportsHybridSearch         => true;  // Weaviate is the only one
    // AI-0036 §9: metadata properties created explicitly with tokenization=field + class-level
    // indexNullState=true, so exact Equal/NotEqual and null-inclusive negation (Ne/Not via De Morgan
    // + Or IsNull) work. Live-verified.
    public bool SupportsMetadataFilters      => true;
    public bool SupportsContinuationToken    => true;  // native cursor
    public bool SupportsPartitionIsolation   => true;
    public bool SupportsDynamicCollections   => true;
    public bool SupportsScoreNormalization   => true;  // cosine

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var envEndpoint = Environment.GetEnvironmentVariable("Koan_TESTS_WEAVIATE")
                          ?? Environment.GetEnvironmentVariable("WEAVIATE_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(envEndpoint) && await Ping(envEndpoint))
        {
            _endpoint = envEndpoint;
            BuildSp();
            IsAvailable = true;
            return;
        }

        try
        {
            // Testcontainers deprecated the parameterless ContainerBuilder ctor; the generic-container
            // pattern (WithImage) still functions — suppress the deprecation (warnings-as-errors).
#pragma warning disable CS0618
            _container = new ContainerBuilder()
                .WithImage("semitechnologies/weaviate:1.25.6")
                .WithEnvironment("QUERY_DEFAULTS_LIMIT", "25")
                .WithEnvironment("AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED", "true")
                .WithEnvironment("AUTOSCHEMA_ENABLED", "true") // auto-create metadata properties on insert (filterable)
                .WithEnvironment("PERSISTENCE_DATA_PATH", "/var/lib/weaviate")
                .WithEnvironment("DEFAULT_VECTORIZER_MODULE", "none")
                .WithEnvironment("CLUSTER_HOSTNAME", "node1")
                .WithEnvironment("RAFT_BOOTSTRAP_EXPECT", "1")
                .WithPortBinding(8080, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(req => req.ForPath("/v1/.well-known/ready").ForPort(8080)))
                .Build();
#pragma warning restore CS0618

            await _container.StartAsync();
            var port = _container.GetMappedPublicPort(8080);
            _endpoint = $"http://localhost:{port}";

            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (await Ping(_endpoint)) { BuildSp(); IsAvailable = true; return; }
                await Task.Delay(500);
            }
            UnavailableReason = "Weaviate container did not respond after 15s.";
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start weaviate container: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        _adminHttp?.Dispose();
        if (_sp is not null) await _sp.DisposeAsync();
        if (_container is not null)
        {
            try { await _container.DisposeAsync(); } catch { }
        }
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (_adminHttp is null || _endpoint is null) return;

        // Drop every existing class in Weaviate's schema for a clean slate. Weaviate creates
        // classes on demand via VectorEnsureCreated, so deleting them is the equivalent of
        // TRUNCATE-by-table for relational adapters.
        try
        {
            using var resp = await _adminHttp.GetAsync("/v1/schema", ct);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("classes", out var classes))
                {
                    foreach (var cls in classes.EnumerateArray())
                    {
                        if (cls.TryGetProperty("class", out var classNameNode) && classNameNode.GetString() is { } className)
                        {
                            using var _ = await _adminHttp.DeleteAsync($"/v1/schema/{Uri.EscapeDataString(className)}", ct);
                        }
                    }
                }
            }
        }
        catch { /* best-effort */ }

        // Rebuild the SP — WeaviateVectorRepository caches `_schemaEnsured` per-instance, and
        // VectorService caches the repo per-(Type, Type). After we just dropped every class,
        // that cache is stale and EnsureCreated would early-return without recreating anything.
        // A fresh SP gives us a fresh VectorService → fresh repo → fresh cache.
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
        services.AddHttpClient();
        services.AddKoanDataVector();

        services.AddOptions<WeaviateOptions>().Configure(o =>
        {
            o.ConnectionString = _endpoint;
            o.Endpoint = _endpoint;
            o.Dimension = EmbeddingDimension;
            o.Metric = "cosine";
        });
        services.AddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<IVectorAdapterFactory, WeaviateVectorAdapterFactory>();

        _sp = services.BuildServiceProvider();
    }

    private static async Task<bool> Ping(string endpoint)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(endpoint, UriKind.Absolute), Timeout = TimeSpan.FromSeconds(2) };
            using var resp = await http.GetAsync("/v1/.well-known/ready");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
