using Koan.Data.Core;
using Koan.Tenancy;

namespace Koan.Identity.Tenancy.Invitations;

/// <summary>The outcome of accepting an invite.</summary>
public enum InviteAcceptOutcome
{
    /// <summary>A new <c>Membership</c> was created for the canonical person.</summary>
    Accepted,
    /// <summary>The person was already a member of the tenant; the invite was consumed idempotently (no duplicate seat).</summary>
    AlreadyMember,
    /// <summary>No invite matched the token.</summary>
    NotFound,
    /// <summary>The invite is expired, revoked, or already accepted.</summary>
    NotRedeemable,
    /// <summary>The signed-in person does not own a verified email matching the invite — refused (anti token-leak).</summary>
    EmailNotOwned,
}

/// <summary>The result of an invite acceptance — the outcome and (when bound) the membership.</summary>
public sealed record InviteAcceptResult(InviteAcceptOutcome Outcome, Membership? Membership);

/// <summary>
/// SEC-0007 P4 — invite binds to the <b>identity</b>, not the email string. At accept-time the membership is bound to
/// the SIGNED-IN canonical person (resolved one layer down via the durable <c>Identity</c> / explicit-link model), so
/// an alias / dotted address / second linked provider can never spawn a duplicate account — the person is already
/// canonical regardless of which email the invite was addressed to, and an existing seat (under ANY alias) is
/// detected and never duplicated. Safe-by-default: the accepter must OWN the invited email (a <b>verified</b>
/// <c>IdentityEmail</c> factor, proven by a prior verified sign-in — never a fresh claim), so a leaked invite link
/// cannot be redeemed by a stranger. Anti-pattern honored: email is never the identity key (it is an ownership proof).
/// </summary>
public sealed class InviteAcceptanceService
{
    /// <summary>
    /// Accept the invite identified by <paramref name="token"/> on behalf of the signed-in canonical person
    /// <paramref name="signedInPersonId"/> (the reconciled <c>Identity.Id</c>, not an email).
    /// </summary>
    public async Task<InviteAcceptResult> AcceptAsync(string token, string signedInPersonId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(signedInPersonId))
            return new InviteAcceptResult(InviteAcceptOutcome.NotFound, null);

        var matches = await Invite.Query(i => i.Token == token, ct).ConfigureAwait(false);
        var invite = matches.FirstOrDefault();
        if (invite is null) return new InviteAcceptResult(InviteAcceptOutcome.NotFound, null);
        if (!invite.IsRedeemable(DateTimeOffset.UtcNow))
            return new InviteAcceptResult(InviteAcceptOutcome.NotRedeemable, null);

        // Email-ownership: the accepter must hold a VERIFIED email factor matching the invite, so a leaked invite link
        // cannot be redeemed by a stranger. Proven by the durable factor (a prior verified sign-in), not a fresh claim.
        if (!await OwnsVerifiedEmailAsync(signedInPersonId, invite.Email, ct).ConfigureAwait(false))
            return new InviteAcceptResult(InviteAcceptOutcome.EmailNotOwned, null);

        // Collision detection: bind to the canonical person, so an existing seat (under any alias) is detected and the
        // invite is consumed idempotently rather than spawning a second membership.
        var existing = await Membership.Query(m => m.IdentityId == signedInPersonId && m.TenantId == invite.TenantId, ct).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            await MarkAcceptedAsync(invite, ct).ConfigureAwait(false);
            return new InviteAcceptResult(InviteAcceptOutcome.AlreadyMember, existing[0]);
        }

        var membership = new Membership
        {
            // Deterministic seat id — a concurrent double-accept converges to ONE upserted row instead of racing two
            // duplicate seats (the existing.Count check alone is a TOCTOU; this closes it at the storage layer).
            Id = Membership.KeyFor(invite.TenantId, signedInPersonId),
            TenantId = invite.TenantId,
            IdentityId = signedInPersonId, // the canonical person id — NEVER the invite email string
            Roles = string.IsNullOrWhiteSpace(invite.Role) ? new List<string>() : new List<string> { invite.Role },
        };
        await membership.Save(ct).ConfigureAwait(false);
        await MarkAcceptedAsync(invite, ct).ConfigureAwait(false);
        return new InviteAcceptResult(InviteAcceptOutcome.Accepted, membership);
    }

    private static async Task<bool> OwnsVerifiedEmailAsync(string personId, string inviteEmail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inviteEmail)) return false;
        var normalized = IdentityEmail.Normalize(inviteEmail); // the one canonical normalization, shared with the factor writer
        var emails = await IdentityEmail.Query(e => e.IdentityId == personId, ct).ConfigureAwait(false);
        return emails.Any(e => e.Verified && string.Equals(e.Address, normalized, StringComparison.Ordinal));
    }

    private static async Task MarkAcceptedAsync(Invite invite, CancellationToken ct)
    {
        if (invite.Status == InviteStatus.Accepted) return;
        invite.Status = InviteStatus.Accepted;
        await invite.Save(ct).ConfigureAwait(false);
    }
}
