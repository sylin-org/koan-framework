using System.Security.Claims;
using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity;
using Koan.Identity.Tenancy;
using Koan.Identity.Tenancy.Deprovisioning;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P4 — atomic verifiable deprovisioning. Deactivation makes "cannot act" true on the REQUEST PATH (the
/// SessionGuard rejects the cookie; sessions are revoked now), seat removal seals the tenant via the fail-closed axis
/// (the middleware stops scoping the person in), and each is recorded by a tamper-evident <see cref="DeprovisioningReceipt"/>.
/// </summary>
[Collection("identity")]
public sealed class DeprovisioningSpec
{
    private readonly IdentityHostFixture _fx;
    public DeprovisioningSpec(IdentityHostFixture fx) => _fx = fx;

    private DeprovisioningService Service => _fx.Services.GetRequiredService<DeprovisioningService>();

    private static ClaimsPrincipal PrincipalFor(string subject)
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, subject) }, "test"));

    [Fact]
    public async Task Deactivation_sets_status_revokes_sessions_and_makes_SessionGuard_reject()
    {
        await new Identity { Id = "dp-user", DisplayName = "Dp", Status = IdentityStatus.Active }.Save();
        await new Session { IdentityId = "dp-user" }.Save();
        await new Session { IdentityId = "dp-user" }.Save();

        var receipt = await Service.DeactivateAsync("dp-user");

        (await Identity.Get("dp-user"))!.Status.Should().Be(IdentityStatus.Deactivated);
        receipt.Kind.Should().Be(DeprovisioningKind.Deactivation);
        receipt.SessionsRevoked.Should().Be(2);
        receipt.StatusSet.Should().Be("Deactivated");
        receipt.Surfaces.Should().Contain(new[] { "data", "storage", "cache", "sessions" });

        // The request-path proof: a deactivated person's principal is rejected at the next validation tick.
        (await SessionGuard.ShouldRejectAsync(PrincipalFor("dp-user")))
            .Should().BeTrue("deactivated = cannot act, enforced on the request path (not a write-only flag)");
    }

    [Fact]
    public async Task The_receipt_is_verifiable_and_detects_tampering()
    {
        await new Identity { Id = "dp-verify", DisplayName = "Verify" }.Save();
        var receipt = await Service.DeactivateAsync("dp-verify");

        var roundTripped = await DeprovisioningReceipt.Get(receipt.Id);
        roundTripped!.Verify().Should().BeTrue("a stored receipt recomputes to its content hash");

        roundTripped.SessionsRevoked = 99; // tamper without re-hashing
        roundTripped.Verify().Should().BeFalse("a mutated field no longer matches the content hash");
    }

    [Fact]
    public async Task Deactivation_is_idempotent()
    {
        await new Identity { Id = "dp-idem", DisplayName = "Idem" }.Save();
        await new Session { IdentityId = "dp-idem" }.Save();

        var first = await Service.DeactivateAsync("dp-idem");
        first.StatusSet.Should().Be("Deactivated");
        first.SessionsRevoked.Should().Be(1);

        var second = await Service.DeactivateAsync("dp-idem");
        second.StatusSet.Should().BeNull("already deactivated — no status change on re-run");
        second.SessionsRevoked.Should().Be(0, "already revoked");
    }

    [Fact]
    public async Task Seat_removal_deletes_the_membership_and_the_middleware_stops_scoping_the_person()
    {
        await new Identity { Id = "dp-seat", DisplayName = "Seat" }.Save();
        await new Membership { TenantId = "dp-tenant", IdentityId = "dp-seat", Roles = { "koan:member" } }.Save();

        var receipt = await Service.RemoveFromTenantAsync("dp-seat", "dp-tenant");

        receipt.Kind.Should().Be(DeprovisioningKind.SeatRemoval);
        receipt.MembershipsRemoved.Should().Be(1);
        receipt.Verify().Should().BeTrue();
        (await Membership.Query(m => m.IdentityId == "dp-seat" && m.TenantId == "dp-tenant")).Should().BeEmpty();

        // The request-path proof: the middleware's membership-authorization now fails, so the tenant can't be scoped in.
        (await TenantResolutionMiddleware.IsMemberAsync("dp-seat", "dp-tenant", default))
            .Should().BeFalse("seat removal seals the tenant on the request path (the fail-closed axis does the rest)");
    }

    [Fact]
    public async Task A_no_op_re_sign_in_does_not_rewrite_the_person_so_a_concurrent_deactivation_is_safe()
    {
        // Deactivation is a read-modify-write; the race that could revert it is a sign-in reconciliation full-row
        // replace from a stale Active snapshot. save-if-changed removes the redundant write in the common case: a
        // returning person with nothing to backfill is NOT re-written, so it cannot clobber a concurrent lifecycle
        // change. Observable proof: a no-op reconcile leaves UpdatedAt untouched.
        await new Identity { Id = "rec-noop", DisplayName = "NoOp" }.Save();
        var before = (await Identity.Get("rec-noop"))!.UpdatedAt;
        await Task.Delay(15);

        var reconciler = _fx.Services.GetRequiredService<IIdentityReconciler>();
        await reconciler.ReconcileAsync(new IdentityClaims("rec-noop", DisplayName: "NoOp", Provider: "dev"));

        (await Identity.Get("rec-noop"))!.UpdatedAt.Should().Be(before,
            "save-if-changed: a no-op reconcile must not full-row-replace the person (which is what could revert a concurrent Deactivation)");
    }

    [Fact]
    public async Task Seat_removal_does_not_touch_the_persons_other_seats_or_sessions()
    {
        await new Identity { Id = "dp-multi", DisplayName = "Multi" }.Save();
        await new Membership { TenantId = "dp-keep", IdentityId = "dp-multi", Roles = { "koan:member" } }.Save();
        await new Membership { TenantId = "dp-drop", IdentityId = "dp-multi", Roles = { "koan:member" } }.Save();
        await new Session { IdentityId = "dp-multi" }.Save();

        await Service.RemoveFromTenantAsync("dp-multi", "dp-drop");

        (await Membership.Query(m => m.IdentityId == "dp-multi" && m.TenantId == "dp-keep")).Should().ContainSingle("the other seat is untouched");
        (await Session.Query(s => s.IdentityId == "dp-multi" && !s.Revoked)).Should().NotBeEmpty("seat removal does not revoke sessions");
        (await Identity.Get("dp-multi"))!.Status.Should().Be(IdentityStatus.Active, "the person persists");
    }
}
