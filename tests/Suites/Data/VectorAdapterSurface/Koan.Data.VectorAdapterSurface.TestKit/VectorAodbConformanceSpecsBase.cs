using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Core;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Tenancy;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Data.VectorAdapterSurface.TestKit;

/// <summary>
/// The one reusable vector AODB conformance ledger (ARCH-0103 §6), the vector-plane twin of the record-plane
/// <c>AodbConformanceSpecsBase</c>. A per-adapter cell subclasses this with a real <c>AddKoan()</c> host (the adapter
/// configured + tenancy + the discoverable <see cref="VectorConformanceShardAxis"/>); the base proves, end-to-end, that
/// the vector plane realizes ALL THREE AODB isolation modes — and that the <see cref="ScopedVectorRepository"/> decorator
/// <b>declares</b> the matching capability tokens.
/// <para>
/// This binds each token to its proof (ARCH-0094, co-defined): the decorator declares
/// <see cref="DataCaps.Isolation.ContainerScoped"/>/<see cref="DataCaps.Isolation.DatabaseScoped"/> (the name-mangling
/// floor) always and <see cref="DataCaps.Isolation.RowScoped"/> when the inner adapter announces metadata filtering — an
/// adapter that declares a mode but does not realize it fails the matching behavioral cell; one that realizes but does
/// not declare fails <see cref="Declares_realized_isolation_modes"/>. Over-claim and under-claim both go red.
/// </para>
/// <para>
/// Note the vector Database mode is the <b>name-fold floor</b> (routed source → distinct collection name) on HTTP
/// adapters and a per-source physical store on the in-process adapters — distinct-source isolation is uniform, but
/// (unlike the record plane) there is no fail-closed-on-unconfigured-source: a name-fold adapter just names a new
/// collection. The Database cell therefore proves distinct-source isolation, not the record plane's external-only throw.
/// </para>
/// </summary>
public abstract class VectorAodbConformanceSpecsBase : IAsyncLifetime
{
    /// <summary>The two routed sources the Database cell drives (the ambient shard values).</summary>
    protected const string SourceA = "vshard_a";
    protected const string SourceB = "vshard_b";

    private static readonly float[] PointA = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
    private static readonly float[] PointB = [0f, 1f, 0f, 0f, 0f, 0f, 0f, 0f];

    protected IntegrationHost? Host { get; private set; }
    private string? _skip;

    /// <summary>
    /// Boot the adapter's <c>AddKoan()</c> conformance host (tenancy + the shard axis discovered). Return a skip reason
    /// (e.g. Docker/backend unavailable) instead of a host to skip the whole ledger. The base sets
    /// <see cref="AppHost.Current"/> and tears the host down.
    /// </summary>
    protected abstract Task<(IntegrationHost? host, string? skip)> BootHostAsync();

    /// <summary>
    /// Hook for adapters whose vector store needs read-your-writes coaxing after a save (e.g. an async index with no
    /// synchronous-refresh knob — Weaviate's HNSW). Called after EACH save with the just-saved <paramref name="id"/> and
    /// its embedding <paramref name="query"/>, in the entity's current ambient scope, so an override can poll the
    /// own-scope search until that id is queryable. Default no-op — the in-process and synchronous-refresh adapters
    /// (RefreshMode/ConsistencyLevel) are immediately consistent.
    /// </summary>
    protected virtual Task SettleAsync<TEntity>(string id, float[] query) where TEntity : class, IEntity<string>
        => Task.CompletedTask;

    /// <summary>
    /// Hook to tear down an external backend (e.g. a Testcontainers container) a subclass started in
    /// <see cref="BootHostAsync"/>. Called by <see cref="DisposeAsync"/> AFTER the host is disposed. Default no-op — the
    /// in-process adapters (InMemoryVector, SqliteVec) have nothing external to stop.
    /// </summary>
    protected virtual ValueTask DisposeBackendAsync() => ValueTask.CompletedTask;

    public async ValueTask InitializeAsync()
    {
        var (host, skip) = await BootHostAsync().ConfigureAwait(false);
        Host = host;
        _skip = skip;
        if (host is not null) AppHost.Current = host.Services;
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is not null)
        {
            if (ReferenceEquals(AppHost.Current, Host.Services)) AppHost.Current = null;
            await Host.DisposeAsync().ConfigureAwait(false);
        }
        await DisposeBackendAsync().ConfigureAwait(false);
    }

    private void SkipIfUnavailable() => Assert.SkipWhen(_skip is not null, _skip ?? "");

    // Probe whether the adapter supports by-id vector retrieval (GetEmbedding). SearchEngine (ElasticSearch/OpenSearch)
    // and Milvus throw NotSupportedException — for them the kNN Search proves isolation alone and the stronger by-id
    // physical-separation check is skipped. Qdrant/Weaviate/InMemoryVector/SqliteVec support it and get the by-id proof.
    private static async Task<bool> SupportsGetEmbedding<TEntity>(string probeId) where TEntity : class, IEntity<string>
    {
        try { await Vector<TEntity>.GetEmbedding(probeId); return true; }
        catch (NotSupportedException) { return false; }
    }

    private IVectorSearchRepository<VectorConformanceTenantDoc, string> ResolveScopedRepo()
    {
        var vectors = (IVectorService)Host!.Services.GetRequiredService(typeof(IVectorService));
        return vectors.TryGetRepository<VectorConformanceTenantDoc, string>()
            ?? throw new InvalidOperationException("No vector adapter resolved for the conformance entity.");
    }

    // ==================== The co-definition: the decorator declares the modes it realizes ====================

    [Fact(DisplayName = "Vector AODB ledger: the ScopedVectorRepository declares the universal floor modes (ContainerScoped + DatabaseScoped); RowScoped is co-defined by the Shared cell")]
    [Trait("Category", "Integration")]
    public void Declares_realized_isolation_modes()
    {
        SkipIfUnavailable();
        // Single-adapter convention: the host must register EXACTLY the adapter under test, so the no-[VectorAdapter]
        // conformance entities cannot silently resolve to a different elected adapter (the misroute hole).
        Host!.Services.GetServices<IVectorAdapterFactory>().Should().HaveCount(1,
            "the conformance host must register exactly one vector adapter (the one under test)");

        var caps = DataCaps.Describe(ResolveScopedRepo(), "scoped-vector");
        // Container + Database are the universal name-mangling floor — realized (and so declared) by EVERY vector adapter.
        caps.Has(DataCaps.Isolation.ContainerScoped).Should().BeTrue("Container mode (the partition name-fold floor) must be declared");
        caps.Has(DataCaps.Isolation.DatabaseScoped).Should().BeTrue("Database mode (the source name-fold floor) must be declared");
        // RowScoped (Shared) is gated on the inner adapter's metadata filtering, so it is NOT universal: a pure-KNN store
        // (e.g. SqliteVec) honestly under-claims it. Its declaration is co-defined BEHAVIORALLY by the Shared cell below
        // (declared ⇒ the overlay isolates; not declared ⇒ a scoped read fails closed) — neither over- nor under-claim
        // can stay green. Asserting it unconditionally here would force an honest non-filtering adapter to over-claim.
    }

    // ==================== Shared (FieldFilter → __koan_tenant overlay stamp + read-filter) ====================

    [Fact(DisplayName = "Vector AODB Shared: RowScoped is co-defined — declared ⇒ the __koan_tenant overlay isolates a kNN; under-claimed ⇒ a scoped read fails closed")]
    [Trait("Category", "Integration")]
    public async Task Shared_tenant_overlay_isolates()
    {
        SkipIfUnavailable();
        var rowScoped = DataCaps.Describe(ResolveScopedRepo(), "scoped-vector").Has(DataCaps.Isolation.RowScoped);

        // The write-stamp persists the managed discriminator regardless of filter support; only the READ filter needs it.
        using (Tenant.Use("acme")) { await Vector<VectorConformanceTenantDoc>.Save("a1", PointA); await SettleAsync<VectorConformanceTenantDoc>("a1", PointA); }
        using (Tenant.Use("globex")) { await Vector<VectorConformanceTenantDoc>.Save("g1", PointB); await SettleAsync<VectorConformanceTenantDoc>("g1", PointB); }

        if (rowScoped)
        {
            // Declared ⇒ the overlay must isolate the kNN (the other tenant's vector is excluded even when nearer).
            using (Tenant.Use("acme"))
                (await Vector<VectorConformanceTenantDoc>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                    .Matches.Select(m => m.Id).Should().Equal("a1");
            using (Tenant.Use("globex"))
                (await Vector<VectorConformanceTenantDoc>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                    .Matches.Select(m => m.Id).Should().Equal("g1");
        }
        else
        {
            // Under-claimed (the inner cannot filter metadata) ⇒ a scoped read must FAIL CLOSED, never silently leak the
            // other tenant's vector. This is the honest path for a pure-KNN store (e.g. SqliteVec).
            Func<Task> scopedRead = async () =>
            {
                using (Tenant.Use("acme"))
                    await Vector<VectorConformanceTenantDoc>.Search(new VectorQueryOptions(Query: PointB, TopK: 10));
            };
            await scopedRead.Should().ThrowAsync<NotSupportedException>();
        }
    }

    // ==================== Container (Particle → distinct physical container per partition) ====================

    [Fact(DisplayName = "Vector AODB Container: a distinct ambient partition resolves to a distinct physical container (no cross-partition leak)")]
    [Trait("Category", "Integration")]
    public async Task Container_partition_isolates()
    {
        SkipIfUnavailable();
        var pA = "vcc-" + Guid.CreateVersion7().ToString("n");
        var pB = "vcc-" + Guid.CreateVersion7().ToString("n");
        using (EntityContext.Partition(pA)) { await Vector<VectorConformancePartitionDoc>.Save("p1", PointA); await SettleAsync<VectorConformancePartitionDoc>("p1", PointA); }
        using (EntityContext.Partition(pB)) { await Vector<VectorConformancePartitionDoc>.Save("p2", PointB); await SettleAsync<VectorConformancePartitionDoc>("p2", PointB); }

        bool byId;
        using (EntityContext.Partition(pA))
        {
            byId = await SupportsGetEmbedding<VectorConformancePartitionDoc>("p1");
            if (byId)
            {
                // Physical separation (by-id): p2 was written under pB, so it is ABSENT from pA's physical container.
                (await Vector<VectorConformancePartitionDoc>.GetEmbedding("p1")).Should().NotBeNull();
                (await Vector<VectorConformancePartitionDoc>.GetEmbedding("p2")).Should().BeNull();
            }
            // kNN: a search in pA returns only pA's vector even when pB's vector is nearer the query.
            (await Vector<VectorConformancePartitionDoc>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("p1");
        }
        using (EntityContext.Partition(pB))
        {
            if (byId) (await Vector<VectorConformancePartitionDoc>.GetEmbedding("p1")).Should().BeNull();
            (await Vector<VectorConformancePartitionDoc>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("p2");
        }
    }

    // ==================== Database (Moniker → per-source physical isolation) ====================

    [Fact(DisplayName = "Vector AODB Database: a Database-mode axis routes by ambient shard to distinct physical isolation per source")]
    [Trait("Category", "Integration")]
    public async Task Database_shard_isolates()
    {
        SkipIfUnavailable();
        using (VectorConformanceShardAmbient.Use(SourceA)) { await Vector<VectorConformanceShardedDoc>.Save("s1", PointA); await SettleAsync<VectorConformanceShardedDoc>("s1", PointA); }
        using (VectorConformanceShardAmbient.Use(SourceB)) { await Vector<VectorConformanceShardedDoc>.Save("s2", PointB); await SettleAsync<VectorConformanceShardedDoc>("s2", PointB); }

        bool byId;
        using (VectorConformanceShardAmbient.Use(SourceA))
        {
            byId = await SupportsGetEmbedding<VectorConformanceShardedDoc>("s1");
            if (byId)
            {
                // Physical separation (by-id): s2 was written under shard B, so it is ABSENT from shard A's physical store.
                (await Vector<VectorConformanceShardedDoc>.GetEmbedding("s1")).Should().NotBeNull();
                (await Vector<VectorConformanceShardedDoc>.GetEmbedding("s2")).Should().BeNull();
            }
            // kNN: a search in shard A returns only shard A's vector even when shard B's vector is nearer the query.
            (await Vector<VectorConformanceShardedDoc>.Search(new VectorQueryOptions(Query: PointB, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("s1");
        }
        using (VectorConformanceShardAmbient.Use(SourceB))
        {
            if (byId) (await Vector<VectorConformanceShardedDoc>.GetEmbedding("s1")).Should().BeNull();
            (await Vector<VectorConformanceShardedDoc>.Search(new VectorQueryOptions(Query: PointA, TopK: 10)))
                .Matches.Select(m => m.Id).Should().Equal("s2");
        }
    }
}
