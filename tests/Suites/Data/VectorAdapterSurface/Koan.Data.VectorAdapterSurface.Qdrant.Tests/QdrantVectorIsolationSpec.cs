using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using Koan.Data.Vector.Connector.Qdrant;
using Koan.Tenancy;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Qdrant.Tests;

/// <summary>
/// ARCH-0103 — the production-adapter AODB isolation proof on a LIVE Qdrant (the lightest vector backend), beyond the
/// custom-SP surface harness: a real <c>AddKoan()</c> host with tenancy + a Database-mode axis. Closes the audit's
/// flagged overlay-naming "live isolation-breach risk" empirically (Shared) and verifies the Container + Database
/// name-mangling floor on a real HTTP vector store.
/// <list type="bullet">
/// <item><b>Shared</b> — the <c>ScopedVectorRepository</c> stamps <c>__koan_tenant</c> into the Qdrant payload
/// (<c>metadata.__koan_tenant</c>, a nested JSON key Qdrant accepts faithfully — unlike Weaviate's GraphQL <c>__</c>
/// reservation) and ANDs it into the kNN filter, so a tenant's search returns only its own vectors even when the OTHER
/// tenant's point is nearer the query. This is the empirical round-trip the map could only reason about.</item>
/// <item><b>Container</b> — a distinct ambient partition resolves to a distinct physical Qdrant collection.</item>
/// <item><b>Database</b> — a Database-mode <c>[Sharded]</c> axis folds the routed source into the collection name
/// (the floor shipped in <c>e196c97c</c>), so each shard is a distinct physical collection on the same Qdrant.</item>
/// </list>
/// Skips when Docker / Qdrant is unreachable.
/// </summary>
public sealed class QdrantVectorIsolationSpec : IAsyncLifetime
{
    [VectorAdapter("qdrant")]
    public sealed class TenantVec : Entity<TenantVec> { }   // tenant-scoped (Shared)

    [HostScoped]                                            // tenancy-exempt: the Container test is partition-only
    [VectorAdapter("qdrant")]
    public sealed class PartVec : Entity<PartVec> { }       // partition-isolated (Container)

    [QdrantSharded]
    [HostScoped]                                            // tenancy-exempt: only the shard axis applies (Database)
    [VectorAdapter("qdrant")]
    public sealed class ShardVec : Entity<ShardVec> { }

    private static readonly float[] PointA = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
    private static readonly float[] PointB = [0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f];

    private IContainer? _qdrant;
    private IntegrationHost? _host;
    private string? _skip;
    private string _endpoint = "";
    private string _resolvedEndpoint = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            _qdrant = new ContainerBuilder("qdrant/qdrant:v1.10.0")
                .WithPortBinding(6333, true)
                .WithPortBinding(6334, true)
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(req => req.ForPort(6333).ForPath("/readyz")))
                .Build();
            await _qdrant.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _skip = $"Qdrant/Docker unavailable: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        var endpoint = $"http://localhost:{_qdrant.GetMappedPublicPort(6333)}";
        _endpoint = endpoint;
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Tenancy:Posture"] = "Closed",
            ["Koan:Data:Qdrant:Endpoint"] = endpoint,
            ["Koan:Data:Qdrant:ConnectionString"] = endpoint,
            ["Koan:Data:Qdrant:Dimension"] = "8",
            ["Koan:Data:Qdrant:WaitForResult"] = "true",   // read-your-writes: deterministic test reads
            ["Koan:Data:Qdrant:DisableAutoDetection"] = "true",   // never let discovery find another local Qdrant
        };

        _host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            // Pin the endpoint deterministically AFTER the connector's configurator (PostConfigure runs last) so a
            // stray local Qdrant (e.g. a dev container on :6333) can never be discovered/used instead of THIS container.
            .ConfigureServices(s =>
            {
                s.AddKoan();
                s.PostConfigure<QdrantOptions>(o =>
                {
                    o.ConnectionString = endpoint;
                    o.Endpoint = endpoint;
                    o.Dimension = 8;
                    o.WaitForResult = true;
                });
            })
            .StartAsync()
            .ConfigureAwait(false);
        AppHost.Current = _host.Services;
        _resolvedEndpoint = _host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<QdrantOptions>>().Value.Endpoint;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            if (ReferenceEquals(AppHost.Current, _host.Services)) AppHost.Current = null;
            await _host.DisposeAsync().ConfigureAwait(false);
        }
        if (_qdrant is not null) { try { await _qdrant.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
    }

    [Fact(DisplayName = "Qdrant Shared: the __koan_tenant payload write-stamp round-trips + isolates a kNN (closes the overlay 'live breach' flag empirically)")]
    [Trait("Category", "Integration")]
    public async Task Shared_tenant_overlay_roundtrips_and_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");
        _resolvedEndpoint.Should().Be(_endpoint, "the adapter must target THIS Testcontainers Qdrant, not a stray local one");

        using (Tenant.Use("acme")) await Vector<TenantVec>.Save("a1", PointA);
        using (Tenant.Use("globex")) await Vector<TenantVec>.Save("g1", PointB);

        // Under acme, query with GLOBEX's point: without the __koan_tenant filter, kNN returns g1 (nearest). With the
        // stamp round-tripping faithfully through Qdrant's payload, the filter excludes g1 — only acme's a1 comes back.
        using (Tenant.Use("acme"))
            (await Vector<TenantVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("a1");
        using (Tenant.Use("globex"))
            (await Vector<TenantVec>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("g1");
    }

    [Fact(DisplayName = "Qdrant Container: a distinct ambient partition resolves to a distinct physical collection (no cross-partition leak)")]
    [Trait("Category", "Integration")]
    public async Task Container_partition_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");

        var pA = "qpc-" + Guid.CreateVersion7().ToString("n");
        var pB = "qpc-" + Guid.CreateVersion7().ToString("n");
        using (EntityContext.Partition(pA)) await Vector<PartVec>.Save("p1", PointA);
        using (EntityContext.Partition(pB)) await Vector<PartVec>.Save("p2", PointB);

        using (EntityContext.Partition(pA))
        {
            (await Vector<PartVec>.GetEmbedding("p1")).Should().NotBeNull();
            (await Vector<PartVec>.GetEmbedding("p2")).Should().BeNull();   // pB's vector lives in another collection
        }
        using (EntityContext.Partition(pB))
            (await Vector<PartVec>.GetEmbedding("p1")).Should().BeNull();
    }

    [Fact(DisplayName = "Qdrant Database: a Database-mode axis folds the routed source into the collection name → distinct physical collection per shard")]
    [Trait("Category", "Integration")]
    public async Task Database_shard_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");

        // No EntityContext.Source — only the ambient shard; the Database-mode axis derives the routed source, which the
        // vector naming folds into the collection name (the source-fold floor). Distinct shards ⇒ distinct collections.
        using (QdrantShardAmbient.Use("alpha")) await Vector<ShardVec>.Save("s1", PointA);
        using (QdrantShardAmbient.Use("beta")) await Vector<ShardVec>.Save("s2", PointB);

        using (QdrantShardAmbient.Use("alpha"))
        {
            (await Vector<ShardVec>.GetEmbedding("s1")).Should().NotBeNull();
            (await Vector<ShardVec>.GetEmbedding("s2")).Should().BeNull();   // beta's vector is in another collection
            (await Vector<ShardVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("s1");
        }
        using (QdrantShardAmbient.Use("beta"))
            (await Vector<ShardVec>.GetEmbedding("s1")).Should().BeNull();
    }
}

// --- A local Database-mode axis for the Database cell (mirrors the Axes.Integration ShardRouteAxis; lives in this
//     assembly so AddKoan discovers it here without coupling to another test project). ---

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class QdrantShardedAttribute : Attribute;

internal static class QdrantShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<QdrantShardedAttribute>(inherit: true) is not null);
}

public static class QdrantShardAmbient
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

public sealed class QdrantShardCarrier : IAmbientSliceCarrier
{
    public string AxisKey => "koan:qdrant-shard";
    public string? Capture() => QdrantShardAmbient.Current is { } s ? "v1:" + s : null;
    public IDisposable Restore(string captured)
        => captured.StartsWith("v1:", StringComparison.Ordinal)
            ? QdrantShardAmbient.Use(captured[3..])
            : throw new InvalidOperationException($"QdrantShardCarrier cannot restore '{captured}'.");
    public IDisposable Suppress() => QdrantShardAmbient.Use(null);
}

public sealed class QdrantShardAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("qdrant-shard")
        .Mode(AxisMode.Database)
        .AppliesTo(QdrantShardMetadata.IsSharded)
        .Field("shard", static () => QdrantShardAmbient.Current, typeof(string))
        .Carries(new QdrantShardCarrier());
}
