using AwesomeAssertions;
using Koan.Data.Core;
using Koan.Identity.Tenancy.Invitations;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// SEC-0007 P4 — invite binds to the canonical PERSON, never the email string. The membership is created for the
/// signed-in identity (so an alias / second linked email can never spawn a duplicate account), the accepter must own
/// a verified email matching the invite (so a leaked link cannot be redeemed by a stranger), and acceptance is
/// idempotent (no duplicate seat).
/// </summary>
[Collection("identity")]
public sealed class InviteAcceptanceSpec : IdentityHostScopedSpec
{
    private readonly IdentityHostFixture _fx;
    public InviteAcceptanceSpec(IdentityHostFixture fx) : base(fx) => _fx = fx;

    private InviteAcceptanceService Invites => _fx.Services.GetRequiredService<InviteAcceptanceService>();

    private static DateTimeOffset Soon => DateTimeOffset.UtcNow.AddDays(7);

    [Fact]
    public async Task Accepting_binds_a_membership_to_the_canonical_person_not_the_email()
    {
        await new Identity { Id = "inv-alice", DisplayName = "Alice" }.Save();
        await new IdentityEmail { IdentityId = "inv-alice", Address = IdentityEmail.Normalize("alice@corp.com"), Verified = true }.Save();
        await new Invite { TenantId = "inv-studio", Email = "Alice@Corp.com", Role = "member", Token = "tok-bind", ExpiresAt = Soon }.Save();

        var result = await Invites.AcceptAsync("tok-bind", "inv-alice");

        result.Outcome.Should().Be(InviteAcceptOutcome.Accepted);
        result.Membership!.IdentityId.Should().Be("inv-alice", "the seat binds to the canonical person id, not the email string");
        result.Membership.TenantId.Should().Be("inv-studio");
        result.Membership.Roles.Should().ContainSingle().Which.Should().Be("member");

        var invite = (await Invite.Query(i => i.Token == "tok-bind")).Single();
        invite.Status.Should().Be(InviteStatus.Accepted, "the invite is consumed");
    }

    [Fact]
    public async Task A_consumed_token_cannot_be_replayed_into_a_second_seat()
    {
        await new Identity { Id = "inv-bob", DisplayName = "Bob" }.Save();
        await new IdentityEmail { IdentityId = "inv-bob", Address = IdentityEmail.Normalize("bob@corp.com"), Verified = true }.Save();
        await new Invite { TenantId = "inv-studio-2", Email = "bob@corp.com", Role = "member", Token = "tok-dup", ExpiresAt = Soon }.Save();

        (await Invites.AcceptAsync("tok-dup", "inv-bob")).Outcome.Should().Be(InviteAcceptOutcome.Accepted);
        // Replaying the SAME (now-consumed) token is refused — a consumed invite is not redeemable.
        (await Invites.AcceptAsync("tok-dup", "inv-bob")).Outcome.Should().Be(InviteAcceptOutcome.NotRedeemable);

        (await Membership.Query(m => m.IdentityId == "inv-bob" && m.TenantId == "inv-studio-2"))
            .Should().ContainSingle("a replayed token must never spawn a duplicate seat");
    }

    [Fact]
    public async Task An_alias_email_resolves_to_the_SAME_person_so_no_duplicate_account()
    {
        // One person, two verified emails (the person ≠ email model). An invite to EITHER binds to the same person —
        // accepting the second never creates a second account/seat.
        await new Identity { Id = "inv-multi", DisplayName = "Multi" }.Save();
        await new IdentityEmail { IdentityId = "inv-multi", Address = IdentityEmail.Normalize("primary@corp.com"), Verified = true }.Save();
        await new IdentityEmail { IdentityId = "inv-multi", Address = IdentityEmail.Normalize("alias@corp.com"), Verified = true }.Save();

        await new Invite { TenantId = "inv-studio-3", Email = "primary@corp.com", Role = "member", Token = "tok-a", ExpiresAt = Soon }.Save();
        await new Invite { TenantId = "inv-studio-3", Email = "alias@corp.com", Role = "admin", Token = "tok-b", ExpiresAt = Soon }.Save();

        (await Invites.AcceptAsync("tok-a", "inv-multi")).Outcome.Should().Be(InviteAcceptOutcome.Accepted);
        (await Invites.AcceptAsync("tok-b", "inv-multi")).Outcome.Should().Be(InviteAcceptOutcome.AlreadyMember,
            "the alias resolves to the same canonical person — no duplicate seat");

        (await Membership.Query(m => m.IdentityId == "inv-multi" && m.TenantId == "inv-studio-3")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_leaked_token_cannot_be_redeemed_by_someone_who_does_not_own_the_email()
    {
        await new Identity { Id = "inv-mallory", DisplayName = "Mallory" }.Save();
        // mallory has NO verified email matching the invite.
        await new Invite { TenantId = "inv-studio-4", Email = "victim@corp.com", Role = "member", Token = "tok-leak", ExpiresAt = Soon }.Save();

        var result = await Invites.AcceptAsync("tok-leak", "inv-mallory");

        result.Outcome.Should().Be(InviteAcceptOutcome.EmailNotOwned, "ownership of the invited email is required (anti token-leak)");
        (await Membership.Query(m => m.IdentityId == "inv-mallory" && m.TenantId == "inv-studio-4")).Should().BeEmpty();
    }

    [Fact]
    public async Task An_UNVERIFIED_email_does_not_satisfy_ownership()
    {
        await new Identity { Id = "inv-unv", DisplayName = "Unverified" }.Save();
        await new IdentityEmail { IdentityId = "inv-unv", Address = IdentityEmail.Normalize("unv@corp.com"), Verified = false }.Save();
        await new Invite { TenantId = "inv-studio-5", Email = "unv@corp.com", Role = "member", Token = "tok-unv", ExpiresAt = Soon }.Save();

        (await Invites.AcceptAsync("tok-unv", "inv-unv")).Outcome.Should().Be(InviteAcceptOutcome.EmailNotOwned);
    }

    [Fact]
    public async Task An_expired_invite_is_not_redeemable()
    {
        await new Identity { Id = "inv-exp", DisplayName = "Exp" }.Save();
        await new IdentityEmail { IdentityId = "inv-exp", Address = IdentityEmail.Normalize("exp@corp.com"), Verified = true }.Save();
        await new Invite { TenantId = "inv-studio-6", Email = "exp@corp.com", Role = "member", Token = "tok-exp", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }.Save();

        (await Invites.AcceptAsync("tok-exp", "inv-exp")).Outcome.Should().Be(InviteAcceptOutcome.NotRedeemable);
    }

    [Fact]
    public async Task An_unknown_token_is_not_found()
        => (await Invites.AcceptAsync("no-such-token", "inv-alice")).Outcome.Should().Be(InviteAcceptOutcome.NotFound);

    [Fact]
    public async Task A_deterministic_seat_id_collapses_a_double_create_to_one_row()
    {
        // The storage-layer backstop for the concurrent double-accept TOCTOU: two saves of the same (tenant, person)
        // seat converge to ONE upserted row instead of racing two duplicates.
        var id = Membership.KeyFor("seat-tnt", "seat-per");
        await new Membership { Id = id, TenantId = "seat-tnt", IdentityId = "seat-per", Roles = { "a" } }.Save();
        await new Membership { Id = id, TenantId = "seat-tnt", IdentityId = "seat-per", Roles = { "b" } }.Save();

        (await Membership.Query(m => m.TenantId == "seat-tnt" && m.IdentityId == "seat-per"))
            .Should().ContainSingle("a deterministic seat id makes the second create upsert the same row");
    }

    [Fact]
    public async Task Concurrent_accepts_of_two_tokens_never_mint_two_seats()
    {
        await new Identity { Id = "inv-conc", DisplayName = "Conc" }.Save();
        await new IdentityEmail { IdentityId = "inv-conc", Address = IdentityEmail.Normalize("conc@corp.com"), Verified = true }.Save();
        await new Invite { TenantId = "inv-studio-conc", Email = "conc@corp.com", Role = "member", Token = "tok-c1", ExpiresAt = Soon }.Save();
        await new Invite { TenantId = "inv-studio-conc", Email = "conc@corp.com", Role = "admin", Token = "tok-c2", ExpiresAt = Soon }.Save();

        // Two distinct redeemable tokens accepted concurrently — the existing-check OR the deterministic seat id keeps
        // it to one seat (the invariant holds regardless of interleaving).
        await Task.WhenAll(
            Invites.AcceptAsync("tok-c1", "inv-conc"),
            Invites.AcceptAsync("tok-c2", "inv-conc"));

        (await Membership.Query(m => m.IdentityId == "inv-conc" && m.TenantId == "inv-studio-conc"))
            .Should().ContainSingle("a concurrent double-accept must never spawn a duplicate seat");
    }
}
