using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Data.Vector.Connector.Milvus;
using Koan.Tenancy;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.Milvus.Tests;

/// <summary>
/// ARCH-0103 — the production-adapter AODB isolation proof on a LIVE Milvus, mirroring
/// <c>QdrantVectorIsolationSpec</c>: a real <c>AddKoan()</c> host with tenancy + a Database-mode axis,
/// beyond the custom-SP surface harness. Closes the audit's flagged overlay-naming "live isolation-breach
/// risk" empirically (Shared) and verifies the Container + Database name-mangling floor on a real Milvus
/// stack (etcd + minio + milvus, exactly as Milvus ships its docker-compose).
/// <list type="bullet">
/// <item><b>Shared</b> — the <c>ScopedVectorRepository</c> stamps <c>__koan_tenant</c> into the Milvus
/// <c>metadata</c> JSON field (a nested key accessed via <c>metadata["__koan_tenant"]</c>) and ANDs it into
/// the kNN filter, so a tenant's search returns only its own vectors even when the OTHER tenant's point is
/// nearer the query. This is the empirical round-trip the fleet map could only reason about.</item>
/// <item><b>Container</b> — a distinct ambient partition resolves to a distinct physical collection.</item>
/// <item><b>Database</b> — a Database-mode <c>[Sharded]</c> axis folds the routed source into the collection
/// name (the source-fold floor shipped in <c>e196c97c</c>), so each shard is a distinct physical collection.</item>
/// </list>
/// This run is also the Docker confirmation of last session's empty-<c>CollectionName</c> fix
/// (<c>8c0f0ad4</c>): a full-AddKoan boot binds an absent <c>CollectionName</c> key to <c>""</c>, and the
/// repo now treats <c>""</c> as null (<c>string.IsNullOrWhiteSpace</c>) so the name is computed from
/// entity + partition + source. Were the fix absent, every entity/partition/shard would collapse onto one
/// empty-named collection and all three cells would fail.
/// Skips when Docker / Milvus is unreachable.
/// </summary>
public sealed class MilvusVectorIsolationSpec : IAsyncLifetime
{
    [VectorAdapter("milvus")]
    public sealed class TenantVec : Entity<TenantVec> { }   // tenant-scoped (Shared)

    [HostScoped]                                            // tenancy-exempt: the Container test is partition-only
    [VectorAdapter("milvus")]
    public sealed class PartVec : Entity<PartVec> { }       // partition-isolated (Container)

    [MilvusSharded]
    [HostScoped]                                            // tenancy-exempt: only the shard axis applies (Database)
    [VectorAdapter("milvus")]
    public sealed class ShardVec : Entity<ShardVec> { }

    private static readonly float[] PointA = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
    private static readonly float[] PointB = [0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f];

    private INetwork? _network;
    private IContainer? _etcd;
    private IContainer? _minio;
    private IContainer? _milvus;
    private IntegrationHost? _host;
    private string? _skip;
    private string _endpoint = "";
    private string _resolvedEndpoint = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            // Milvus 2.4 standalone wants three real services on a shared network — etcd + minio + milvus —
            // exactly like its official docker-compose (mirrors MilvusTestFactory). Image-constructor overload
            // avoids the CS0618-obsolete parameterless ContainerBuilder().
            _network = new NetworkBuilder().Build();
            await _network.CreateAsync().ConfigureAwait(false);

            _etcd = new ContainerBuilder("quay.io/coreos/etcd:v3.5.5")
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
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("ready to serve client requests"))
                .Build();
            await _etcd.StartAsync().ConfigureAwait(false);

            _minio = new ContainerBuilder("minio/minio:RELEASE.2023-03-20T20-16-18Z")
                .WithNetwork(_network)
                .WithNetworkAliases("minio")
                .WithEnvironment("MINIO_ACCESS_KEY", "minioadmin")
                .WithEnvironment("MINIO_SECRET_KEY", "minioadmin")
                .WithCommand("minio", "server", "/minio_data")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("API:"))
                .Build();
            await _minio.StartAsync().ConfigureAwait(false);

            _milvus = new ContainerBuilder("milvusdb/milvus:v2.4.13")
                .WithNetwork(_network)
                .WithNetworkAliases("milvus")
                .WithEnvironment("ETCD_ENDPOINTS", "etcd:2379")
                .WithEnvironment("MINIO_ADDRESS", "minio:9000")
                .WithCommand("milvus", "run", "standalone")
                .WithPortBinding(19530, true)
                .WithPortBinding(9091, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Proxy successfully started"))
                .Build();
            await _milvus.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _skip = $"Milvus/Docker unavailable: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        var endpoint = $"http://localhost:{_milvus.GetMappedPublicPort(19530)}";
        _endpoint = endpoint;
        var settings = new System.Collections.Generic.Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Koan:Environment"] = "Test",
            ["Koan:Orchestration:ForceOrchestrationMode"] = "Standalone",
            ["Koan:Data:Tenancy:Posture"] = "Closed",
            ["Koan:Data:Milvus:Endpoint"] = endpoint,
            ["Koan:Data:Milvus:ConnectionString"] = endpoint,
            ["Koan:Data:Milvus:Dimension"] = "8",
            ["Koan:Data:Milvus:Metric"] = "COSINE",
            ["Koan:Data:Milvus:ConsistencyLevel"] = "Strong",        // read-your-writes: inserts visible to search
            ["Koan:Data:Milvus:DisableAutoDetection"] = "true",      // never let discovery find another local Milvus
        };

        _host = await KoanIntegrationHost.Configure()
            .WithSettings(settings)
            // Pin the endpoint deterministically AFTER the connector's configurator (PostConfigure runs last) so a
            // stray local Milvus can never be discovered/used instead of THIS container. CollectionName is left
            // unset so partition + source folding governs the name (the empty-CollectionName fix from 8c0f0ad4).
            .ConfigureServices(s =>
            {
                s.AddKoan();
                s.PostConfigure<MilvusOptions>(o =>
                {
                    o.ConnectionString = endpoint;
                    o.Endpoint = endpoint;
                    o.Dimension = 8;
                    o.Metric = "COSINE";
                    o.ConsistencyLevel = "Strong";
                });
            })
            .StartAsync()
            .ConfigureAwait(false);
        AppHost.Current = _host.Services;
        _resolvedEndpoint = _host.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<MilvusOptions>>().Value.Endpoint;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            if (ReferenceEquals(AppHost.Current, _host.Services)) AppHost.Current = null;
            await _host.DisposeAsync().ConfigureAwait(false);
        }
        if (_milvus is not null) { try { await _milvus.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
        if (_minio is not null) { try { await _minio.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
        if (_etcd is not null) { try { await _etcd.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
        if (_network is not null) { try { await _network.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
    }

    [Fact(DisplayName = "Milvus Shared: the __koan_tenant metadata write-stamp round-trips + isolates a kNN (closes the overlay 'live breach' flag empirically)")]
    [Trait("Category", "Integration")]
    public async Task Shared_tenant_overlay_roundtrips_and_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");
        _resolvedEndpoint.Should().Be(_endpoint, "the adapter must target THIS Testcontainers Milvus, not a stray local one");

        using (Tenant.Use("acme")) { await Vector<TenantVec>.EnsureCreated(); await Vector<TenantVec>.Save("a1", PointA); }
        using (Tenant.Use("globex")) await Vector<TenantVec>.Save("g1", PointB);

        // Under acme, query with GLOBEX's point: without the __koan_tenant filter, kNN returns g1 (nearest). With the
        // stamp round-tripping faithfully through Milvus's metadata JSON field, the filter excludes g1 — only a1 returns.
        using (Tenant.Use("acme"))
            (await Vector<TenantVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("a1");
        using (Tenant.Use("globex"))
            (await Vector<TenantVec>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("g1");
    }

    [Fact(DisplayName = "Milvus Container: a distinct ambient partition resolves to a distinct physical collection (no cross-partition leak)")]
    [Trait("Category", "Integration")]
    public async Task Container_partition_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");

        var pA = "mvpc" + Guid.CreateVersion7().ToString("n");
        var pB = "mvpc" + Guid.CreateVersion7().ToString("n");
        using (EntityContext.Partition(pA)) { await Vector<PartVec>.EnsureCreated(); await Vector<PartVec>.Save("p1", PointA); }
        using (EntityContext.Partition(pB)) { await Vector<PartVec>.EnsureCreated(); await Vector<PartVec>.Save("p2", PointB); }

        // Searching pA toward p2's vector still returns only p1 — p2 lives in pB's collection, unreachable from pA.
        using (EntityContext.Partition(pA))
            (await Vector<PartVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("p1");
        using (EntityContext.Partition(pB))
            (await Vector<PartVec>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("p2");
    }

    [Fact(DisplayName = "Milvus Database: a Database-mode axis folds the routed source into the collection name → distinct physical collection per shard")]
    [Trait("Category", "Integration")]
    public async Task Database_shard_isolates()
    {
        Assert.SkipWhen(_skip is not null, _skip ?? "");

        // No EntityContext.Source — only the ambient shard; the Database-mode axis derives the routed source, which the
        // vector naming folds into the collection name (the source-fold floor). Distinct shards ⇒ distinct collections.
        using (MilvusShardAmbient.Use("alpha")) { await Vector<ShardVec>.EnsureCreated(); await Vector<ShardVec>.Save("s1", PointA); }
        using (MilvusShardAmbient.Use("beta")) { await Vector<ShardVec>.EnsureCreated(); await Vector<ShardVec>.Save("s2", PointB); }

        using (MilvusShardAmbient.Use("alpha"))
            (await Vector<ShardVec>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("s1");
        using (MilvusShardAmbient.Use("beta"))
            (await Vector<ShardVec>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("s2");
    }
}

// --- A local Database-mode axis for the Database cell (mirrors the Axes.Integration ShardRouteAxis; lives in this
//     assembly so AddKoan discovers it here without coupling to another test project). ---

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class MilvusShardedAttribute : Attribute;

internal static class MilvusShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<MilvusShardedAttribute>(inherit: true) is not null);
}

public static class MilvusShardAmbient
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

public sealed class MilvusShardCarrier : IAmbientSliceCarrier
{
    public string AxisKey => "koan:milvus-shard";
    public string? Capture() => MilvusShardAmbient.Current is { } s ? "v1:" + s : null;
    public IDisposable Restore(string captured)
        => captured.StartsWith("v1:", StringComparison.Ordinal)
            ? MilvusShardAmbient.Use(captured[3..])
            : throw new InvalidOperationException($"MilvusShardCarrier cannot restore '{captured}'.");
    public IDisposable Suppress() => MilvusShardAmbient.Use(null);
}

public sealed class MilvusShardAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("milvus-shard")
        .Mode(AxisMode.Database)
        .AppliesTo(MilvusShardMetadata.IsSharded)
        .Field("shard", static () => MilvusShardAmbient.Current, typeof(string))
        .Carries(new MilvusShardCarrier());
}
