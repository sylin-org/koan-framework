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
using Koan.Data.Connector.ElasticSearch;
using Koan.Data.Core;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Tenancy;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.ElasticSearch.Tests;

/// <summary>
/// ARCH-0103 — the production-adapter AODB isolation proof on a LIVE Elasticsearch, mirroring
/// <c>QdrantVectorIsolationSpec</c>: a real <c>AddKoan()</c> host with tenancy + a Database-mode axis,
/// beyond the custom-SP surface harness. Closes the audit's flagged overlay-naming "live isolation-breach
/// risk" empirically (Shared) and verifies the Container + Database name-mangling floor on a real
/// search-engine vector store.
/// <list type="bullet">
/// <item><b>Shared</b> — the <c>ScopedVectorRepository</c> stamps <c>__koan_tenant</c> into the Elasticsearch
/// <c>metadata</c> sub-document (a nested JSON key ES accepts faithfully) and ANDs it into the kNN pre-filter,
/// so a tenant's search returns only its own vectors even when the OTHER tenant's point is nearer the query.
/// This is the empirical round-trip the fleet map could only reason about.</item>
/// <item><b>Container</b> — a distinct ambient partition resolves to a distinct physical index.</item>
/// <item><b>Database</b> — a Database-mode <c>[Sharded]</c> axis folds the routed source into the index name
/// (the source-fold floor shipped in <c>e196c97c</c>), so each shard is a distinct physical index.</item>
/// </list>
/// Skips when Docker / Elasticsearch is unreachable.
/// </summary>
public sealed class ElasticSearchVectorIsolationSpec : IAsyncLifetime
{
    [VectorAdapter("elasticsearch")]
    public sealed class TenantVec : Entity<TenantVec> { }   // tenant-scoped (Shared)

    [HostScoped]                                            // tenancy-exempt: the Container test is partition-only
    [VectorAdapter("elasticsearch")]
    public sealed class PartVec : Entity<PartVec> { }       // partition-isolated (Container)

    [ElasticSharded]
    [HostScoped]                                            // tenancy-exempt: only the shard axis applies (Database)
    [VectorAdapter("elasticsearch")]
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
            // Testcontainers 4.11+). Env + wait strategy mirror ElasticSearchTestFactory.
            _container = new ContainerBuilder("docker.elastic.co/elasticsearch/elasticsearch:8.13.4")
                .WithEnvironment("discovery.type", "single-node")
                .WithEnvironment("xpack.security.enabled", "false")
                .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
                .WithPortBinding(9200, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(req => req.ForPath("/_cluster/health").ForPort(9200)))
                .Build();
            await _container.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _skip = $"Elasticsearch/Docker unavailable: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        var endpoint = $"http://localhost:{_container.GetMappedPublicPort(9200)}";
        _endpoint = endpoint;
        var settings = new System.Collections.Generic.Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Tenancy:Posture"] = "Closed",
            ["Koan:Data:ElasticSearch:Endpoint"] = endpoint,
            ["Koan:Data:ElasticSearch:ConnectionString"] = endpoint,
            ["Koan:Data:ElasticSearch:Dimension"] = "8",
            ["Koan:Data:ElasticSearch:RefreshMode"] = "true",            // read-your-writes: synchronous refresh
            ["Koan:Data:ElasticSearch:DisableAutoDetection"] = "true",   // never let discovery find another local Elasticsearch
        };

        _host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            // Pin the endpoint deterministically AFTER the connector's configurator (PostConfigure runs last) so a
            // stray local Elasticsearch can never be discovered/used instead of THIS container.
            .ConfigureServices(s =>
            {
                s.AddKoan();
                s.PostConfigure<ElasticSearchOptions>(o =>
                {
                    o.ConnectionString = endpoint;
                    o.Endpoint = endpoint;
                    o.Dimension = 8;
                    o.RefreshMode = "true";
                });
            })
            .StartAsync()
            .ConfigureAwait(false);
        AppHost.Current = _host.Services;
        _resolvedEndpoint = _host.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<ElasticSearchOptions>>().Value.Endpoint;
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

    [Fact(DisplayName = "Elasticsearch Shared: the __koan_tenant metadata write-stamp round-trips + isolates a kNN (closes the overlay 'live breach' flag empirically)")]
    [Trait("Category", "Integration")]
    public async Task Shared_tenant_overlay_roundtrips_and_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");
        _resolvedEndpoint.Should().Be(_endpoint, "the adapter must target THIS Testcontainers Elasticsearch, not a stray local one");

        using (Tenant.Use("acme")) { await Vector<TenantVec>.EnsureCreated(); await Vector<TenantVec>.Save("a1", PointA); }
        using (Tenant.Use("globex")) await Vector<TenantVec>.Save("g1", PointB);

        // Under acme, query with GLOBEX's point: without the __koan_tenant filter, kNN returns g1 (nearest). With the
        // stamp round-tripping faithfully through Elasticsearch's metadata, the filter excludes g1 — only acme's a1 comes back.
        using (Tenant.Use("acme"))
            (await Vector<TenantVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("a1");
        using (Tenant.Use("globex"))
            (await Vector<TenantVec>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("g1");
    }

    [Fact(DisplayName = "Elasticsearch Container: a distinct ambient partition resolves to a distinct physical index (no cross-partition leak)")]
    [Trait("Category", "Integration")]
    public async Task Container_partition_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");

        var pA = "espc-" + Guid.CreateVersion7().ToString("n");
        var pB = "espc-" + Guid.CreateVersion7().ToString("n");
        using (EntityContext.Partition(pA)) { await Vector<PartVec>.EnsureCreated(); await Vector<PartVec>.Save("p1", PointA); }
        using (EntityContext.Partition(pB)) { await Vector<PartVec>.EnsureCreated(); await Vector<PartVec>.Save("p2", PointB); }

        // Searching pA toward p2's vector still returns only p1 — p2 lives in pB's index, unreachable from pA.
        using (EntityContext.Partition(pA))
            (await Vector<PartVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("p1");
        using (EntityContext.Partition(pB))
            (await Vector<PartVec>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("p2");
    }

    [Fact(DisplayName = "Elasticsearch Database: a Database-mode axis folds the routed source into the index name → distinct physical index per shard")]
    [Trait("Category", "Integration")]
    public async Task Database_shard_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");

        // No EntityContext.Source — only the ambient shard; the Database-mode axis derives the routed source, which the
        // vector naming folds into the index name (the source-fold floor). Distinct shards ⇒ distinct indexes.
        using (ElasticShardAmbient.Use("alpha")) { await Vector<ShardVec>.EnsureCreated(); await Vector<ShardVec>.Save("s1", PointA); }
        using (ElasticShardAmbient.Use("beta")) { await Vector<ShardVec>.EnsureCreated(); await Vector<ShardVec>.Save("s2", PointB); }

        using (ElasticShardAmbient.Use("alpha"))
            (await Vector<ShardVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("s1");
        using (ElasticShardAmbient.Use("beta"))
            (await Vector<ShardVec>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("s2");
    }
}

// --- A local Database-mode axis for the Database cell (mirrors the Axes.Integration ShardRouteAxis; lives in this
//     assembly so AddKoan discovers it here without coupling to another test project). ---

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class ElasticShardedAttribute : Attribute;

internal static class ElasticShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<ElasticShardedAttribute>(inherit: true) is not null);
}

public static class ElasticShardAmbient
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

public sealed class ElasticShardCarrier : IAmbientSliceCarrier
{
    public string AxisKey => "koan:elastic-shard";
    public string? Capture() => ElasticShardAmbient.Current is { } s ? "v1:" + s : null;
    public IDisposable Restore(string captured)
        => captured.StartsWith("v1:", StringComparison.Ordinal)
            ? ElasticShardAmbient.Use(captured[3..])
            : throw new InvalidOperationException($"ElasticShardCarrier cannot restore '{captured}'.");
    public IDisposable Suppress() => ElasticShardAmbient.Use(null);
}

public sealed class ElasticShardAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("elastic-shard")
        .Mode(AxisMode.Database)
        .AppliesTo(ElasticShardMetadata.IsSharded)
        .Field("shard", static () => ElasticShardAmbient.Current, typeof(string))
        .Carries(new ElasticShardCarrier());
}
