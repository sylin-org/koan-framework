using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core.Capabilities;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Filtering;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Model;
using Koan.Data.Core.Pipeline;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Tenancy.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// GAP C 0.3 — vector tenant-isolation, through a real <c>AddKoan()</c> boot (ARCH-0079) on the no-Docker InMemory
/// vector adapter (which announces <c>VectorCaps.Filters</c>). The <c>ScopedVectorRepository</c> decorator stamps
/// the ambient <c>__koan_tenant</c> into the vector metadata on write and ANDs a <c>__koan_tenant == &lt;tenant&gt;</c>
/// predicate into every search, so a KNN returns only the active tenant's vectors — even when the query vector is
/// nearer the OTHER tenant's point (proving the metadata filter, not the query, does the isolation). All tenants
/// share one InMemory bucket (it partitions by <c>EntityContext.Partition</c>, not the tenant), so the filter is the
/// only thing standing between them. (Replaces the prior blanket fail-closed-under-scope; Weaviate/Qdrant/sqlite-vec
/// ride the same decorator — the adapter only needs to announce metadata filtering.)
/// </summary>
public sealed class VectorTenantIsolationSpec
{
    private static IReadOnlyDictionary<string, string?> Posture(string p)
        => new Dictionary<string, string?> { ["Koan:Data:Tenancy:Posture"] = p };

    [VectorAdapter("inmemory")]
    public sealed class VecDoc : Entity<VecDoc> { public string Title { get; set; } = ""; }

    private static readonly float[] AcmePoint = [1f, 0f, 0f];
    private static readonly float[] GlobexPoint = [0f, 1f, 0f];

    [Fact(DisplayName = "vector isolation: a KNN returns only the active tenant's vectors (even when the query is nearer the other tenant's point)")]
    public async Task Vector_search_is_tenant_isolated()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        using (Tenant.Use("acme")) await Vector<VecDoc>.Save("a1", AcmePoint);
        using (Tenant.Use("globex")) await Vector<VecDoc>.Save("g1", GlobexPoint);

        // Under acme, query with GLOBEX's point: without the scope filter, KNN would return g1 (the nearest). With
        // isolation, the __koan_tenant filter excludes g1, so only acme's a1 comes back.
        using (Tenant.Use("acme"))
        {
            var r = await Vector<VecDoc>.Search(new VectorQueryOptions(Query: GlobexPoint, TopK: 10));
            r.Matches.Select(m => m.Id).Should().Equal("a1");
        }
        using (Tenant.Use("globex"))
        {
            var r = await Vector<VecDoc>.Search(new VectorQueryOptions(Query: AcmePoint, TopK: 10));
            r.Matches.Select(m => m.Id).Should().Equal("g1");
        }
    }

    [Fact(DisplayName = "vector isolation: a vector op with NO tenant in scope fails closed (Closed posture) — not an unfiltered search / unstamped write")]
    public async Task Vector_op_with_no_tenant_fails_closed()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        // No tenant in scope under Closed: a tenant-scoped vector write/search must FAIL CLOSED via the reused
        // IStorageGuard (the convergence fix) — previously Search fell through to an UNFILTERED KNN (a cross-tenant
        // leak, since writes are now stamped) and the write landed unscoped.
        var write = async () => await Vector<VecDoc>.Save("x", AcmePoint);
        await write.Should().ThrowAsync<InvalidOperationException>();

        var search = async () => await Vector<VecDoc>.Search(new VectorQueryOptions(Query: AcmePoint, TopK: 10));
        await search.Should().ThrowAsync<InvalidOperationException>();
    }

    // --- The contributor-agnostic proof: a NON-equality read-contributor isolates vectors with ZERO vector code. ---

    private static readonly AsyncLocal<bool> _moderator = new();
    private static readonly AsyncLocal<string?> _writeVis = new();

    private static IDisposable Set<T>(AsyncLocal<T> slot, T value)
    {
        var prev = slot.Value;
        slot.Value = value;
        return new Pop(() => slot.Value = prev!);
    }
    private sealed class Pop(Action undo) : IDisposable { public void Dispose() => undo(); }

    // [HostScoped] so the TENANT axis does not apply — this entity carries ONLY the fake non-equality moderation axis.
    [VectorAdapter("inmemory")]
    [HostScoped]
    public sealed class ModDoc : Entity<ModDoc> { }

    private static void RegisterModAxis() => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
        StorageName: "__vis",
        ClrType: typeof(string),
        ValueProvider: () => _writeVis.Value,
        AppliesTo: t => t == typeof(ModDoc),
        RequiredCapability: DataCaps.Isolation.RowScoped,
        AutoReadFilter: false));   // non-equality axis (supplies its own predicate via the contributor below)

    private sealed class ModReadContributor : IReadFilterContributor
    {
        public Filter? ReadFilter(Type t)
        {
            if (t != typeof(ModDoc)) return null;
            if (_moderator.Value) return null;                                            // a moderator sees everything
            return Filter.On(FieldPath.Of("__vis"), FilterOperator.Ne, FilterValue.Of("hidden"));
        }
        public Capability? RequiredCapability => DataCaps.Isolation.RowScoped;
        public bool ExcludesFromCache(Type t) => t == typeof(ModDoc);
    }

    [Fact(DisplayName = "vector realignment: a NON-equality (moderation) IReadFilterContributor isolates a KNN — zero vector-specific axis code")]
    public async Task Vector_honors_a_non_equality_read_contributor()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(
            extraSettings: Posture("Closed"),
            configureServices: s => s.AddSingleton<IReadFilterContributor>(new ModReadContributor()));
        RegisterModAxis();
        try
        {
            runtime.ResetEntityCaches();

            using (Set(_writeVis, "hidden")) await Vector<ModDoc>.Save("h1", AcmePoint);
            using (Set(_writeVis, "public")) await Vector<ModDoc>.Save("p1", GlobexPoint);

            // A non-moderator KNN: the contributor's non-equality predicate (__vis != hidden) hides h1 — vector folds the
            // registered IReadFilterContributor through the SAME ReadScopeFold the data path uses; no equality assumption.
            var r = await Vector<ModDoc>.Search(new VectorQueryOptions(Query: AcmePoint, TopK: 10));
            r.Matches.Select(m => m.Id).Should().Equal("p1");

            // A moderator sees both (the contributor returns no predicate).
            using (Set(_moderator, true))
            {
                var rm = await Vector<ModDoc>.Search(new VectorQueryOptions(Query: AcmePoint, TopK: 10));
                rm.Matches.Select(m => m.Id).Should().BeEquivalentTo(new[] { "h1", "p1" });
            }
        }
        finally
        {
            _moderator.Value = false;
            _writeVis.Value = null;
            ManagedFieldRegistry.Reset();   // the tenancy registrar re-registers __koan_tenant on the next boot (idempotent)
        }
    }

    [Fact(DisplayName = "vector isolation: a user filter composes with the scope filter (AND), still tenant-isolated")]
    public async Task User_filter_composes_with_scope()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();

        var md = new Dictionary<string, object> { ["kind"] = "report" };
        using (Tenant.Use("acme")) await Vector<VecDoc>.Save("a1", AcmePoint, md);
        using (Tenant.Use("globex")) await Vector<VecDoc>.Save("g1", GlobexPoint, md);

        // A user metadata filter (kind == report) ANDs with the scope filter; globex's g1 (same kind) is still excluded.
        using (Tenant.Use("acme"))
        {
            var r = await Vector<VecDoc>.Search(GlobexPoint, filter: new Dictionary<string, object> { ["kind"] = "report" }, topK: 10);
            r.Matches.Select(m => m.Id).Should().Equal("a1");
        }
    }
}
