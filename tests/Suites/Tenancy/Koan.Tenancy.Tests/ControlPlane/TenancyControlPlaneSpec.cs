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
/// ARCH-0099 §2/§4a — the durable control-plane registry entities (<see cref="TenantRecord"/>,
/// <see cref="Membership"/>, <see cref="Invite"/>), proven through a real <c>AddKoan()</c> boot (ARCH-0079). All
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
        t.Status.Should().Be(TenantStatus.Active);
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
    public async Task Invite_round_trips_and_redeemability_reflects_status_and_expiry()
    {
        await using var runtime = await TenancyRuntimeFixture.CreateAsync();
        runtime.ResetEntityCaches();
        using var _ = Iso();

        var now = DateTimeOffset.UtcNow;
        var inv = await new Invite
        {
            TenantId = "t1",
            Email = "x@acme.dev",
            Role = "member",
            Token = "tok-123",
            ExpiresAt = now.AddDays(7),
        }.Save();

        var loaded = await Invite.Get(inv.Id);
        loaded!.Status.Should().Be(InviteStatus.Pending);
        loaded.IsRedeemable(now).Should().BeTrue();
        loaded.IsRedeemable(now.AddDays(8)).Should().BeFalse(); // past expiry

        loaded.Status = InviteStatus.Revoked;
        loaded.IsRedeemable(now).Should().BeFalse(); // not pending
    }
}
