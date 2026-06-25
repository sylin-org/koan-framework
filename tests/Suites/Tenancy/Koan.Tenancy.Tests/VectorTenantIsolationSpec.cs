using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Koan.Tenancy.Tests.Support;
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
