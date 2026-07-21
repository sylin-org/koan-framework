using System;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Tenancy;
using Koan.Tenancy.Tests.Support;
using Xunit;

namespace Koan.Tenancy.Tests;

/// <summary>
/// The durable control-plane registry entities (<see cref="TenantRecord"/> and <see cref="Membership"/>), proven
/// through a real <c>AddKoan()</c> boot. Both
/// are <c>[HostScoped]</c>: they persist in the host/root scope, exempt from tenant isolation, so the registry
/// that defines tenants is reachable regardless of which tenant is in ambient scope.
/// </summary>
public sealed class TenancyControlPlaneSpec
{
    private static IDisposable Iso() => EntityContext.Partition("p" + Guid.CreateVersion7().ToString("n"));

    [Fact]
    public async Task TenantRecord_persists_host_scoped_and_is_visible_under_any_tenant()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync();
        runtime.ResetEntityCaches();
        using var _ = Iso();

        var t = await new TenantRecord { Name = "Acme" }.Save();
        t.Id.Should().NotBeNullOrEmpty();
        t.CreatedAt.Should().NotBe(default);

        // [HostScoped] → the registry row is visible regardless of the ambient tenant.
        using (Tenant.Use("some-other-tenant"))
            (await TenantRecord.Get(t.Id))!.Name.Should().Be("Acme");
    }

    [Fact]
    public async Task Membership_carries_roles_on_the_membership_and_is_queryable_by_tenant()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync();
        runtime.ResetEntityCaches();
        using var _ = Iso();

        await new Membership { TenantId = "t1", IdentityId = "leo@acme.dev", Roles = { TenancyRoles.Owner } }.Save();
        await new Membership { TenantId = "t1", IdentityId = "jane@acme.dev", Roles = { "member" } }.Save();
        await new Membership { TenantId = "t2", IdentityId = "leo@acme.dev", Roles = { "member" } }.Save();

        var t1 = (await Membership.All()).Where(m => m.TenantId == "t1").ToList();
        t1.Should().HaveCount(2);
        t1.Single(m => m.IdentityId == "leo@acme.dev").IsOwner.Should().BeTrue();
        t1.Single(m => m.IdentityId == "jane@acme.dev").IsOwner.Should().BeFalse();

        // The same identity has a DIFFERENT (non-owner) role in another tenant — roles are per-membership.
        (await Membership.All()).Single(m => m.TenantId == "t2").HasRole("member").Should().BeTrue();
    }

    [Fact]
    public void Membership_key_is_stable_for_one_subject_and_tenant()
        => Membership.KeyFor("acme", "person-1")
            .Should().Be(Membership.KeyFor("acme", "person-1"))
            .And.NotBe(Membership.KeyFor("globex", "person-1"));
}
