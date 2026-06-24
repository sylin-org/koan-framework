using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.SoftDelete;
using Koan.Tenancy;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// ARCH-0101 §4/§10 — the TWO-AXIS composition proof the soft-delete review flagged as uncovered: tenant × soft-delete
/// together, through a real <c>AddKoan()</c> boot (ARCH-0079) with BOTH <c>Koan.Tenancy</c> and <c>Koan.Data.SoftDelete</c>
/// referenced. The load-bearing isolation promise: a soft-delete <c>Delete</c> stays tenant-scoped, and the
/// <c>.HardDelete()</c> bypass is plane-specific — it still cannot reach another tenant's row. Plus the fail-closed
/// proof: a <c>[SoftDelete]</c> entity on a non-isolating adapter (JSON) fails closed cleanly rather than throwing mid-read.
/// </summary>
public sealed class TenantSoftDeleteSpec
{
    private static IReadOnlyDictionary<string, string?> Posture(string posture)
        => new Dictionary<string, string?> { ["Koan:Data:Tenancy:Posture"] = posture };

    private static IDisposable Isolate() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    // tenant + soft-delete: both the __koan_tenant and __deleted managed axes apply.
    [SoftDelete]
    public sealed class TDoc : Entity<TDoc> { public string Title { get; set; } = ""; }

    // soft-delete only (host-scoped ⇒ no tenant axis) — isolates the soft-delete fail-closed on a non-isolating adapter.
    [HostScoped]
    [SoftDelete]
    public sealed class HDoc : Entity<HDoc> { public string Title { get; set; } = ""; }

    [Fact(DisplayName = "tenant × soft-delete: Delete soft-removes only the caller's row; other tenants are untouched and can't see it")]
    public async Task Soft_delete_stays_tenant_scoped()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        TDoc a1, a2, b1;
        using (Tenant.Use("acme")) { a1 = await new TDoc { Title = "a1" }.Save(); a2 = await new TDoc { Title = "a2" }.Save(); }
        using (Tenant.Use("globex")) b1 = await new TDoc { Title = "b1" }.Save();

        using (Tenant.Use("acme")) await TDoc.Remove(a1.Id);   // soft-delete acme's a1

        using (Tenant.Use("acme")) (await TDoc.All()).Select(d => d.Id).Should().Equal(a2.Id);             // a1 hidden
        using (Tenant.Use("acme")) using (TDoc.WithDeleted()) (await TDoc.All()).Select(d => d.Id).Should().BeEquivalentTo(new[] { a1.Id, a2.Id });  // recycle bin
        using (Tenant.Use("globex")) (await TDoc.All()).Select(d => d.Id).Should().Equal(b1.Id);           // globex untouched

        // Cross-tenant: globex can NEVER see acme's soft-deleted row — not even with the recycle bin open.
        using (Tenant.Use("globex")) using (TDoc.WithDeleted()) (await TDoc.Get(a1.Id)).Should().BeNull();
    }

    [Fact(DisplayName = "tenant × soft-delete: .HardDelete() is plane-specific — it still cannot reach another tenant's row")]
    public async Task HardDelete_stays_tenant_scoped()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"));
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        TDoc a1;
        using (Tenant.Use("acme")) a1 = await new TDoc { Title = "a1" }.Save();

        // globex tries to hard-delete acme's row by guessing its id — the bypass skips ONLY the soft-delete override,
        // the tenant read-scoping is RETAINED, so the physical delete cannot find (let alone remove) acme's row.
        using (Tenant.Use("globex")) (await new TDoc { Id = a1.Id, Title = "x" }.HardDelete()).Should().BeFalse();
        using (Tenant.Use("acme")) (await TDoc.Get(a1.Id)).Should().NotBeNull();   // acme's row survives
    }

    [Fact(DisplayName = "soft-delete fails closed on a non-isolating adapter (JSON) — clean 'does not announce', not an opaque mid-read throw")]
    public async Task Soft_delete_fails_closed_on_a_non_isolating_adapter()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync(extraSettings: Posture("Closed"), adapter: "json");
        runtime.ResetEntityCaches();
        using var _iso = Isolate();

        // HDoc is [HostScoped] (no tenant axis) + [SoftDelete]. The soft-delete managed field requires RowScoped, which
        // JSON does not announce, so even an empty read fails closed cleanly rather than throwing an opaque managed-field error.
        var act = async () => await HDoc.All();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*does not announce*");
    }
}
