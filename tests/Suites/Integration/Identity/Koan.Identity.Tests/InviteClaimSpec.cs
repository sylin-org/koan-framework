using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity;
using Koan.Identity.Tenancy.Invitations;
using Koan.Tenancy;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// PMC-035: the invitation claim ceremony. The invitation row itself is the contested resource —
/// two identities racing one token converge to one claimant and one seat; a claimant whose run was
/// interrupted re-drives the same token idempotently; revocation, expiry, email assurance, and
/// reserved roles each fail closed without consuming anything.
/// </summary>
[Collection("identity")]
public sealed class InviteClaimSpec(IdentityHostFixture fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private IServiceProvider Services => fixture.Services;

    private static async Task<(string IdentityId, string Email)> NewVerifiedPersonAsync(
        IServiceProvider services, string email)
    {
        var person = await new Identity { DisplayName = email }.Save();
        await new IdentityEmail
        {
            IdentityId = person.Id,
            Address = IdentityEmail.Normalize(email),
            Verified = true,
            Primary = true,
        }.Save();
        return (person.Id, IdentityEmail.Normalize(email));
    }

    private static IssuedInvite Issue(IServiceProvider services, string email, string role = "editor",
        DateTimeOffset? expiresAt = null)
    {
        var issuance = (InviteIssuanceService)services.GetService(typeof(InviteIssuanceService))!;
        var tenantId = NewTenantAsync(services).GetAwaiter().GetResult();
        return issuance.IssueAsync("operator", tenantId, email, role, expiresAt).GetAwaiter().GetResult();
    }

    private static async Task<string> NewTenantAsync(IServiceProvider services)
    {
        var tenant = await new TenantRecord
        {
            Name = $"invite-{Guid.CreateVersion7():n}",
            Code = Guid.CreateVersion7().ToString("n")[..12],
        }.Save();
        return tenant.Id;
    }

    [Fact]
    public async Task Two_identities_racing_one_token_yield_one_seat_and_one_claimant()
    {
        var services = Services;
        var acceptance = (InviteAcceptanceService)services.GetService(typeof(InviteAcceptanceService))!;

        // One verified inbox, duplicated across two identities — the reconciler can legitimately
        // produce this shape, and it is exactly the leaked-token scenario the claim must survive.
        var email = $"race-{Guid.CreateVersion7():n}@example.com";
        var (alice, _) = await NewVerifiedPersonAsync(services, email);
        var (bob, _) = await NewVerifiedPersonAsync(services, email);
        alice.Should().NotBe(bob);

        var issued = Issue(services, email);

        var races = await Task.WhenAll(
            acceptance.AcceptAsync(issued.Token, alice),
            acceptance.AcceptAsync(issued.Token, bob));

        races.Count(result => result.Outcome == InviteAcceptOutcome.Accepted).Should().Be(1,
            "exactly one identity wins the claim conditional write");
        races.Count(result => result.Outcome == InviteAcceptOutcome.AlreadyClaimed).Should().Be(1);

        var seats = await Membership.Query(m => m.TenantId == issued.Invite.TenantId);
        seats.Should().ContainSingle("one seat, no matter how the race interleaves");
        seats[0].IdentityId.Should().Be(races.Single(r => r.TookSeat).Membership!.IdentityId);

        var stored = await TenantInvite.Get(issued.Invite.Id);
        stored!.Status.Should().Be(TenantInviteStatus.Accepted);
        stored.ClaimedBy.Should().Be(races.Single(r => r.TookSeat).Membership!.IdentityId);
    }

    [Fact]
    public async Task An_interrupted_claim_is_recovered_by_its_own_claimant_only()
    {
        var services = Services;
        var acceptance = (InviteAcceptanceService)services.GetService(typeof(InviteAcceptanceService))!;

        var email = $"recover-{Guid.CreateVersion7():n}@example.com";
        var (person, _) = await NewVerifiedPersonAsync(services, email);
        var (other, _) = await NewVerifiedPersonAsync(services, $"other-{Guid.CreateVersion7():n}@example.com");
        var issued = Issue(services, email);

        // Simulate a crash after the claim but before the seat: forge exactly that stored state.
        var claimed = await TenantInvite.Get(issued.Invite.Id);
        claimed!.Status = TenantInviteStatus.Claimed;
        claimed.ClaimedBy = person;
        claimed.ClaimedAt = DateTimeOffset.UtcNow;
        claimed.ClaimAttempts = 1;
        await claimed.Save();

        // A peer cannot continue someone else's claim…
        var intruder = await acceptance.AcceptAsync(issued.Token, other);
        intruder.Outcome.Should().Be(InviteAcceptOutcome.AlreadyClaimed);

        // …but the claimant's retry converges: one seat, completed invitation.
        var recovered = await acceptance.AcceptAsync(issued.Token, person);
        recovered.Outcome.Should().Be(InviteAcceptOutcome.Accepted);
        recovered.Membership!.IdentityId.Should().Be(person);

        var seats = await Membership.Query(m => m.TenantId == issued.Invite.TenantId);
        seats.Should().ContainSingle();
        (await TenantInvite.Get(issued.Invite.Id))!.Status.Should().Be(TenantInviteStatus.Accepted);
    }

    [Fact]
    public async Task Revocation_races_the_claim_and_one_side_wins_honestly()
    {
        var services = Services;
        var issuance = (InviteIssuanceService)services.GetService(typeof(InviteIssuanceService))!;
        var acceptance = (InviteAcceptanceService)services.GetService(typeof(InviteAcceptanceService))!;

        var email = $"revoke-{Guid.CreateVersion7():n}@example.com";
        var (person, _) = await NewVerifiedPersonAsync(services, email);
        var issued = Issue(services, email);

        // Revoke before any claim: acceptance reports it and consumes nothing.
        (await issuance.RevokeAsync("operator", issued.Invite.Id)).Should().BeTrue();
        var rejected = await acceptance.AcceptAsync(issued.Token, person);
        rejected.Outcome.Should().Be(InviteAcceptOutcome.Revoked);
        (await Membership.Query(m => m.TenantId == issued.Invite.TenantId)).Should().BeEmpty();

        // Revoking an accepted invitation is refused — the seat already exists. (The live invite must
        // carry the person's OWN verified email so the acceptance actually completes.)
        var live = Issue(services, email);
        await acceptance.AcceptAsync(live.Token, person);
        (await issuance.RevokeAsync("operator", live.Invite.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Expired_and_unverified_and_wrong_owner_each_fail_without_consuming()
    {
        var services = Services;
        var acceptance = (InviteAcceptanceService)services.GetService(typeof(InviteAcceptanceService))!;

        // Expired: the guard says no and the row keeps its Pending state.
        var expiredEmail = $"expired-{Guid.CreateVersion7():n}@example.com";
        var (holder, _) = await NewVerifiedPersonAsync(services, expiredEmail);
        var expired = Issue(services, expiredEmail, expiresAt: Now - TimeSpan.FromMinutes(1));
        (await acceptance.AcceptAsync(expired.Token, holder))
            .Outcome.Should().Be(InviteAcceptOutcome.Expired);
        (await TenantInvite.Get(expired.Invite.Id))!.Status.Should().Be(TenantInviteStatus.Pending);

        // Unverified inbox: the claimant owns the address but only as unverified — no claim.
        var unprovenEmail = $"unproven-{Guid.CreateVersion7():n}@example.com";
        var unverified = await new Identity { DisplayName = "unproven" }.Save();
        await new IdentityEmail
        {
            IdentityId = unverified.Id,
            Address = IdentityEmail.Normalize(unprovenEmail),
            Verified = false,
        }.Save();
        var invite = Issue(services, unprovenEmail);
        (await acceptance.AcceptAsync(invite.Token, unverified.Id))
            .Outcome.Should().Be(InviteAcceptOutcome.EmailNotOwned);
        (await TenantInvite.Get(invite.Invite.Id))!.Status.Should().Be(TenantInviteStatus.Pending);

        // Wrong owner: a verified identity that does not own the invited address at all.
        var strangerEmail = $"stranger-{Guid.CreateVersion7():n}@example.com";
        var (stranger, _) = await NewVerifiedPersonAsync(services, strangerEmail);
        var target = Issue(services, $"target-{Guid.CreateVersion7():n}@example.com");
        (await acceptance.AcceptAsync(target.Token, stranger))
            .Outcome.Should().Be(InviteAcceptOutcome.EmailNotOwned);
    }

    [Fact]
    public async Task A_second_accept_by_the_same_person_is_reported_not_duplicated()
    {
        var services = Services;
        var acceptance = (InviteAcceptanceService)services.GetService(typeof(InviteAcceptanceService))!;

        var email = $"repeat-{Guid.CreateVersion7():n}@example.com";
        var (person, _) = await NewVerifiedPersonAsync(services, email);
        var issued = Issue(services, email);

        var first = await acceptance.AcceptAsync(issued.Token, person);
        first.Outcome.Should().Be(InviteAcceptOutcome.Accepted);

        var replay = await acceptance.AcceptAsync(issued.Token, person);
        replay.Outcome.Should().Be(InviteAcceptOutcome.AlreadyMember);
        replay.Membership!.Id.Should().Be(first.Membership!.Id);

        (await Membership.Query(m => m.TenantId == issued.Invite.TenantId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Reserved_host_roles_are_refused_at_issue()
    {
        var services = Services;
        var issuance = (InviteIssuanceService)services.GetService(typeof(InviteIssuanceService))!;

        var tenantId = await NewTenantAsync(services);
        var act = () => issuance.IssueAsync(
            "operator", tenantId, $"host-{Guid.CreateVersion7():n}@example.com", TenancyRoles.Operator);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*reserved host role*");
    }
}
