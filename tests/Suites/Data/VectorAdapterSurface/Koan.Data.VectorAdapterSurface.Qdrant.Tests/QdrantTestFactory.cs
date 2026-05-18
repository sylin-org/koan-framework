using System.Net.Http;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Qdrant;
using Koan.Data.VectorAdapterSurface.TestKit;

namespace Koan.Data.VectorAdapterSurface.Qdrant.Tests;

/// <summary>
/// Qdrant cell of the vector matrix. Single-binary deployment — no etcd/minio sidecars like
/// Milvus — which makes the Testcontainers setup trivial. <c>/readyz</c> flips to 200 once
/// the node is accepting queries, and HTTP-from-host works reliably across Docker Desktop on
/// Windows because Qdrant doesn't do any inter-container chatter that gets in the way of port
/// mapping.
///
/// <para>
/// Operational defaults: <see cref="QdrantOptions.Dimension"/> defaults to 1536 (OpenAI ada-002
/// / text-embedding-3-small size). <see cref="QdrantOptions.WaitForResult"/> is true, which
/// means writes block until visible to subsequent reads — that's what gives this adapter its
/// <see cref="SupportsDeleteImmediatelyVisibleToSearch"/> = true claim.
/// </para>
/// </summary>
public sealed class QdrantTestFactory : IVectorAdapterTestFactory
{
    private IContainer? _qdrant;
    private ServiceProvider? _sp;
    private string? _endpoint;
    private HttpClient? _adminHttp;
    private bool _initialized;

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason { get; private set; }
    public IServiceProvider Services => _sp ?? throw new InvalidOperationException("Factory not initialized.");
    public int EmbeddingDimension => 8;

    // Implemented natively — Qdrant exposes /points/{id}?with_vector for single fetch and
    // /points (POST) with ids[] + with_vector for batch fetch.
    public bool SupportsGetEmbedding         => true;
    public bool SupportsBulkOperations       => true;
    public bool SupportsFlush                => true;  // adapter overrides: drops the collection
    public bool SupportsExportAll            => true;  // /points/scroll pagination
    public bool SupportsHybridSearch         => false;
    public bool SupportsMetadataFilters      => true;  // must/should/must_not via QdrantFilterTranslator
    public bool SupportsContinuationToken    => false; // search doesn't paginate (scroll is a separate endpoint)
    public bool SupportsPartitionIsolation   => true;
    public bool SupportsDynamicCollections   => true;
    // Qdrant's wait=true write semantics make this trivially true — unlike Milvus.
    public bool SupportsDeleteImmediatelyVisibleToSearch => true;
    public bool SupportsScoreNormalization   => true;  // cosine

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var envEndpoint = Environment.GetEnvironmentVariable("Koan_TESTS_QDRANT")
                          ?? Environment.GetEnvironmentVariable("QDRANT_ENDPOINT")
                          ?? Environment.GetEnvironmentVariable("QDRANT_URL");
        if (!string.IsNullOrWhiteSpace(envEndpoint) && await Ping(envEndpoint))
        {
            _endpoint = envEndpoint;
            BuildSp();
            IsAvailable = true;
            return;
        }

        try
        {
            // v1.10.x is the latest 1.x stable line; 1.11+ are also fine but pinning gives
            // deterministic CI behavior. Qdrant follows semver rigorously so REST surface is
            // stable across 1.x. Using the image-constructor overload (the parameterless
            // ContainerBuilder() is now CS0618-obsolete in Testcontainers 4.11+).
            _qdrant = new ContainerBuilder("qdrant/qdrant:v1.10.0")
                .WithPortBinding(6333, true)
                .WithPortBinding(6334, true)
                // /readyz returns 200 once the node has loaded collections and is accepting
                // queries — that's the right "ready" signal. HTTP-from-host works reliably
                // because Qdrant is a single process with direct port binding.
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(req => req.ForPort(6333).ForPath("/readyz")))
                .Build();
            await _qdrant.StartAsync();

            var port = _qdrant.GetMappedPublicPort(6333);
            _endpoint = $"http://localhost:{port}";
            BuildSp();
            IsAvailable = true;
            return;
        }
        catch (Exception ex)
        {
            UnavailableReason = $"Failed to start Qdrant: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        _adminHttp?.Dispose();
        if (_sp is not null) await _sp.DisposeAsync();
        if (_qdrant is not null) { try { await _qdrant.DisposeAsync(); } catch { } }
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        if (_adminHttp is not null && _endpoint is not null)
        {
            try
            {
                // Drop every existing collection. /collections returns the full list, then
                // DELETE /collections/{name} for each. Best-effort — failures during reset
                // shouldn't crash subsequent specs.
                using var listResp = await _adminHttp.GetAsync("/collections", ct);
                if (listResp.IsSuccessStatusCode)
                {
                    var json = await listResp.Content.ReadAsStringAsync(ct);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("result", out var result) &&
                        result.TryGetProperty("collections", out var collections))
                    {
                        foreach (var col in collections.EnumerateArray())
                        {
                            if (col.TryGetProperty("name", out var nameElem) &&
                                nameElem.GetString() is { } name)
                            {
                                using var _ = await _adminHttp.DeleteAsync($"/collections/{Uri.EscapeDataString(name)}", ct);
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
        services.AddHttpClient("qdrant", c => c.BaseAddress = new Uri(_endpoint));
        services.AddKoanDataVector();

        services.AddOptions<QdrantOptions>().Configure(o =>
        {
            o.ConnectionString = _endpoint;
            o.Endpoint = _endpoint;
            o.Dimension = EmbeddingDimension;
            o.AutoCreateCollection = true;
            o.Distance = "Cosine";
            o.WaitForResult = true; // synchronous writes for deterministic test behavior
        });
        services.AddSingleton<IStorageNameResolver, DefaultStorageNameResolver>();
        services.AddSingleton<IVectorAdapterFactory, QdrantVectorAdapterFactory>();

        _sp = services.BuildServiceProvider();
    }

    private static async Task<bool> Ping(string endpoint)
    {
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(endpoint, UriKind.Absolute), Timeout = TimeSpan.FromSeconds(2) };
            using var resp = await http.GetAsync("/readyz");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
