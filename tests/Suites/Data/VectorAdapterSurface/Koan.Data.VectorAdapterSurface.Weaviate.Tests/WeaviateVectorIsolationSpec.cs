using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Weaviate;
using Koan.Tenancy;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Weaviate.Tests;

/// <summary>
/// ARCH-0103 — the production-adapter AODB isolation proof on a LIVE Weaviate, mirroring
/// <c>QdrantVectorIsolationSpec</c>: a real <c>AddKoan()</c> host with tenancy + a Database-mode axis,
/// beyond the custom-SP surface harness. Where <see cref="WeaviateOverlayIsolationSpec"/> proves the
/// <c>__ → koan_</c> overlay rename in isolation, this proves the full tenancy + Container + Database
/// stack end-to-end on Weaviate's GraphQL surface (the one store that RESERVES a leading <c>__</c>).
/// <list type="bullet">
/// <item><b>Shared</b> — the <c>ScopedVectorRepository</c> renames <c>__koan_tenant</c> to the declared
/// <c>koan_koan_tenant</c> property (Weaviate's GraphQL reserves <c>__</c>) and ANDs it into the kNN filter,
/// so a tenant's search returns only its own vectors even when the OTHER tenant's point is nearer.</item>
/// <item><b>Container</b> — a distinct ambient partition resolves to a distinct physical Weaviate class.</item>
/// <item><b>Database</b> — a Database-mode <c>[Sharded]</c> axis folds the routed source into the class name
/// (the source-fold floor shipped in <c>e196c97c</c>), so each shard is a distinct physical class.</item>
/// </list>
/// Skips when Docker / Weaviate is unreachable.
/// </summary>
public sealed class WeaviateVectorIsolationSpec : IAsyncLifetime
{
    [VectorAdapter("weaviate")]
    public sealed class TenantVec : Entity<TenantVec> { }   // tenant-scoped (Shared)

    [HostScoped]                                            // tenancy-exempt: the Container test is partition-only
    [VectorAdapter("weaviate")]
    public sealed class PartVec : Entity<PartVec> { }       // partition-isolated (Container)

    [WeaviateSharded]
    [HostScoped]                                            // tenancy-exempt: only the shard axis applies (Database)
    [VectorAdapter("weaviate")]
    public sealed class ShardVec : Entity<ShardVec> { }

    private static readonly float[] PointA = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
    private static readonly float[] PointB = [0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f];

    private IContainer? _container;
    private IntegrationHost? _host;
    private string? _skip;
    private string _endpoint = "";
    private string _resolvedEndpoint = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            // Image-constructor overload (the parameterless ContainerBuilder() is CS0618-obsolete in
            // Testcontainers 4.11+). Env + wait strategy mirror WeaviateTestFactory.
            _container = new ContainerBuilder("semitechnologies/weaviate:1.25.6")
                .WithEnvironment("QUERY_DEFAULTS_LIMIT", "25")
                .WithEnvironment("AUTHENTICATION_ANONYMOUS_ACCESS_ENABLED", "true")
                .WithEnvironment("AUTOSCHEMA_ENABLED", "true") // auto-create metadata properties on insert (filterable)
                .WithEnvironment("PERSISTENCE_DATA_PATH", "/var/lib/weaviate")
                .WithEnvironment("DEFAULT_VECTORIZER_MODULE", "none")
                .WithEnvironment("CLUSTER_HOSTNAME", "node1")
                .WithEnvironment("RAFT_BOOTSTRAP_EXPECT", "1")
                .WithPortBinding(8080, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(req => req.ForPath("/v1/.well-known/ready").ForPort(8080)))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _skip = $"Weaviate/Docker unavailable: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        var endpoint = $"http://localhost:{_container.GetMappedPublicPort(8080)}";
        _endpoint = endpoint;
        var settings = new System.Collections.Generic.Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Tenancy:Posture"] = "Closed",
            ["Koan:Data:Weaviate:Endpoint"] = endpoint,
            ["Koan:Data:Weaviate:ConnectionString"] = endpoint,
            ["Koan:Data:Weaviate:Dimension"] = "8",
            ["Koan:Data:Weaviate:Metric"] = "cosine",
            ["Koan:Data:Weaviate:DisableAutoDetection"] = "true",   // never let discovery (or a ZenGarden offering) override the pin
        };

        _host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            // Pin the endpoint deterministically AFTER the connector's configurator (PostConfigure runs last) so a
            // stray local Weaviate can never be discovered/used instead of THIS container.
            .ConfigureServices(s =>
            {
                s.AddKoan();
                s.PostConfigure<WeaviateOptions>(o =>
                {
                    o.ConnectionString = endpoint;
                    o.Endpoint = endpoint;
                    o.Dimension = 8;
                    o.Metric = "cosine";
                });
            })
            .StartAsync()
            .ConfigureAwait(false);
        AppHost.Current = _host.Services;
        _resolvedEndpoint = _host.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<WeaviateOptions>>().Value.Endpoint;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            if (ReferenceEquals(AppHost.Current, _host.Services)) AppHost.Current = null;
            await _host.DisposeAsync().ConfigureAwait(false);
        }
        if (_container is not null) { try { await _container.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
    }

    // Weaviate builds its HNSW vector index asynchronously and exposes no synchronous-refresh knob (unlike
    // ES/OS RefreshMode or Milvus ConsistencyLevel), so a search immediately after an insert can transiently
    // return empty. Each cell polls the entity's OWN-scope search until its vector is indexed BEFORE asserting
    // isolation. This tolerates the async index WITHOUT masking a leak: once both vectors are confirmed indexed,
    // a cross-scope search that still surfaced a foreign vector (a real leak) fails the subsequent Equal assertion.
    private static async Task WaitIndexed<T>(float[] query, string id) where T : class, IEntity<string>
    {
        for (var attempt = 0; attempt < 40; attempt++)   // up to ~10s
        {
            var ids = (await Vector<T>.Search(new VectorQueryOptions(Query: query, TopK: 10))).Matches.Select(m => m.Id);
            if (ids.Contains(id)) return;
            await Task.Delay(250);
        }
    }

    [Fact(DisplayName = "Weaviate Shared: the renamed koan_koan_tenant write-stamp round-trips + isolates a kNN (the __→koan_ overlay end-to-end under tenancy)")]
    [Trait("Category", "Integration")]
    public async Task Shared_tenant_overlay_roundtrips_and_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");
        _resolvedEndpoint.Should().Be(_endpoint, "the adapter must target THIS Testcontainers Weaviate, not a stray local one");

        using (Tenant.Use("acme")) { await Vector<TenantVec>.EnsureCreated(); await Vector<TenantVec>.Save("a1", PointA); await WaitIndexed<TenantVec>(PointA, "a1"); }
        using (Tenant.Use("globex")) { await Vector<TenantVec>.Save("g1", PointB); await WaitIndexed<TenantVec>(PointB, "g1"); }

        // Under acme, query with GLOBEX's point: without the renamed koan_koan_tenant filter working, kNN returns g1
        // (nearest). With the rename round-tripping through Weaviate's GraphQL, the filter excludes g1 — only a1 returns.
        using (Tenant.Use("acme"))
            (await Vector<TenantVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("a1");
        using (Tenant.Use("globex"))
            (await Vector<TenantVec>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("g1");
    }

    [Fact(DisplayName = "Weaviate Container: a distinct ambient partition resolves to a distinct physical class (no cross-partition leak)")]
    [Trait("Category", "Integration")]
    public async Task Container_partition_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");

        var pA = "wvpc" + Guid.CreateVersion7().ToString("n");
        var pB = "wvpc" + Guid.CreateVersion7().ToString("n");
        using (EntityContext.Partition(pA)) { await Vector<PartVec>.EnsureCreated(); await Vector<PartVec>.Save("p1", PointA); await WaitIndexed<PartVec>(PointA, "p1"); }
        using (EntityContext.Partition(pB)) { await Vector<PartVec>.EnsureCreated(); await Vector<PartVec>.Save("p2", PointB); await WaitIndexed<PartVec>(PointB, "p2"); }

        // Searching pA toward p2's vector still returns only p1 — p2 lives in pB's class, unreachable from pA.
        using (EntityContext.Partition(pA))
            (await Vector<PartVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("p1");
        using (EntityContext.Partition(pB))
            (await Vector<PartVec>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("p2");
    }

    [Fact(DisplayName = "Weaviate Database: a Database-mode axis folds the routed source into the class name → distinct physical class per shard")]
    [Trait("Category", "Integration")]
    public async Task Database_shard_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");

        // No EntityContext.Source — only the ambient shard; the Database-mode axis derives the routed source, which the
        // vector naming folds into the class name (the source-fold floor). Distinct shards ⇒ distinct classes.
        using (WeaviateShardAmbient.Use("alpha")) { await Vector<ShardVec>.EnsureCreated(); await Vector<ShardVec>.Save("s1", PointA); await WaitIndexed<ShardVec>(PointA, "s1"); }
        using (WeaviateShardAmbient.Use("beta")) { await Vector<ShardVec>.EnsureCreated(); await Vector<ShardVec>.Save("s2", PointB); await WaitIndexed<ShardVec>(PointB, "s2"); }

        using (WeaviateShardAmbient.Use("alpha"))
            (await Vector<ShardVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("s1");
        using (WeaviateShardAmbient.Use("beta"))
            (await Vector<ShardVec>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("s2");
    }
}

// --- A local Database-mode axis for the Database cell (mirrors the Axes.Integration ShardRouteAxis; lives in this
//     assembly so AddKoan discovers it here without coupling to another test project). ---

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class WeaviateShardedAttribute : Attribute;

internal static class WeaviateShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<WeaviateShardedAttribute>(inherit: true) is not null);
}

public static class WeaviateShardAmbient
{
    private static readonly AsyncLocal<string?> _shard = new();
    public static string? Current => _shard.Value;
    public static IDisposable Use(string? shard)
    {
        var prev = _shard.Value;
        _shard.Value = shard;
        return new Scope(prev);
    }
    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _done;
        public void Dispose() { if (_done) return; _done = true; _shard.Value = previous; }
    }
}

public sealed class WeaviateShardCarrier : IAmbientSliceCarrier
{
    public string AxisKey => "koan:weaviate-shard";
    public string? Capture() => WeaviateShardAmbient.Current is { } s ? "v1:" + s : null;
    public IDisposable Restore(string captured)
        => captured.StartsWith("v1:", StringComparison.Ordinal)
            ? WeaviateShardAmbient.Use(captured[3..])
            : throw new InvalidOperationException($"WeaviateShardCarrier cannot restore '{captured}'.");
    public IDisposable Suppress() => WeaviateShardAmbient.Use(null);
}

public sealed class WeaviateShardAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("weaviate-shard")
        .Mode(AxisMode.Database)
        .AppliesTo(WeaviateShardMetadata.IsSharded)
        .Field("shard", static () => WeaviateShardAmbient.Current, typeof(string))
        .Carries(new WeaviateShardCarrier());
}
