using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using Koan.Identity;
using Koan.Identity.Tenancy.Invitations;
using Koan.Tenancy;
using SnapVault.Models;

namespace SnapVault.Services;

/// <summary>The result of a studio issuing a gallery invite — the accept token (embed in the client's link) + the ids.</summary>
public sealed record GalleryInviteTicket(string Token, string InviteId, string EventId);

/// <summary>The outcome of a guest accepting a gallery invite.</summary>
public sealed record GalleryAcceptResult(InviteAcceptOutcome Outcome, GalleryGrant? Grant);

/// <summary>
/// The studio↔client gallery lifecycle. A studio invites a client (by email) to ONE event's gallery; the client
/// accepts while signed in — proving verified-email ownership via the shipped invite-binds-to-identity primitive
/// (<see cref="InviteAcceptanceService"/>) — which mints a <see cref="GalleryGrant"/>, the fail-closed source the
/// SEC-0008 access axis reads to scope the guest's photo reads to that event. Nothing bespoke: the identity binding is
/// the framework primitive; the read scoping is the data-layer access axis.
/// </summary>
public sealed class GalleryInviteService
{
    private readonly InviteAcceptanceService _accept;
    public GalleryInviteService(InviteAcceptanceService accept) => _accept = accept;

    /// <summary>
    /// Studio invites <paramref name="email"/> to <paramref name="eventId"/>'s gallery with a guest
    /// <paramref name="role"/> ("proofer" = view + select + comment, the default proofing share; "viewer" = view
    /// only). The role is carried on the <see cref="GalleryInvite"/> and mapped to the grant's permissions on accept.
    /// </summary>
    public async Task<GalleryInviteTicket> InviteAsync(
        string studioTenantId, string eventId, string email, string role = "proofer", string? invitedBy = null,
        TimeSpan? lifetime = null, CancellationToken ct = default)
    {
        var invite = new Invite
        {
            TenantId = studioTenantId,
            Email = IdentityEmail.Normalize(email),
            Role = "guest",
            Token = Guid.NewGuid().ToString("n"),
            ExpiresAt = DateTimeOffset.UtcNow + (lifetime ?? TimeSpan.FromDays(30)),
        };
        await invite.Save(ct);

        var gi = new GalleryInvite
        {
            Id = GalleryInvite.KeyFor(invite.Id),
            InviteId = invite.Id,
            EventId = eventId,
            StudioTenantId = studioTenantId,
            GuestRole = role,
            InvitedBy = invitedBy,
        };
        await gi.Save(ct);

        return new GalleryInviteTicket(invite.Token, invite.Id, eventId);
    }

    /// <summary>Map a guest role to the grant's permission set. "viewer" = view only; anything else = proofer.</summary>
    private static List<string> PermissionsForRole(string? role) =>
        string.Equals(role, "viewer", StringComparison.OrdinalIgnoreCase)
            ? new List<string> { "view" }
            : new List<string> { "view", "select", "comment" };

    /// <summary>
    /// The signed-in guest <paramref name="signedInGuestId"/> accepts the invite <paramref name="token"/>. On success
    /// (verified-email ownership enforced by <see cref="InviteAcceptanceService"/>) mints/refreshes the gallery grant.
    /// A leaked link redeemed by the wrong person returns <see cref="InviteAcceptOutcome.EmailNotOwned"/> and no grant.
    /// </summary>
    public async Task<GalleryAcceptResult> AcceptAsync(string token, string signedInGuestId, CancellationToken ct = default)
    {
        var result = await _accept.AcceptAsync(token, signedInGuestId, ct);
        if (result.Outcome is not (InviteAcceptOutcome.Accepted or InviteAcceptOutcome.AlreadyMember))
            return new GalleryAcceptResult(result.Outcome, null);

        // The control-plane Invite carries no event; the GalleryInvite (keyed off the invite id) binds it to the set.
        var invite = (await Invite.Query(i => i.Token == token, ct)).FirstOrDefault();
        if (invite is null) return new GalleryAcceptResult(result.Outcome, null);
        var gi = await GalleryInvite.Get(GalleryInvite.KeyFor(invite.Id));
        if (gi is null) return new GalleryAcceptResult(result.Outcome, null);

        // Deterministic (guest,event) grant id ⇒ a re-accept converges instead of duplicating a seat. The grant's
        // permissions are mapped from the invite's GuestRole (viewer vs proofer) — the guest-write floor enforces them.
        var grant = new GalleryGrant
        {
            Id = GalleryGrant.KeyFor(signedInGuestId, gi.EventId),
            IdentityId = signedInGuestId,
            EventId = gi.EventId,
            StudioTenantId = gi.StudioTenantId,
            Permissions = PermissionsForRole(gi.GuestRole),
        };
        await grant.Save(ct);
        return new GalleryAcceptResult(result.Outcome, grant);
    }
}
