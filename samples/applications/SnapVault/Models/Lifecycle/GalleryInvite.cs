using System;
using Koan.Core;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Tenancy;

namespace SnapVault.Models;

/// <summary>
/// Binds a control-plane <c>Invite</c> token to a specific shareable SET (an <see cref="Event"/>) in a studio —
/// the studio↔client lifecycle's "share photos of an event with the client" record.
/// <para>
/// [HostScoped] control-plane row (matching the shipped <c>Invite</c>/<c>Membership</c> pattern): a guest accepting
/// an invite has no ambient studio tenant, so <see cref="StudioTenantId"/> + <see cref="EventId"/> are carried
/// explicitly. The shipped <c>Invite</c> owns email/role/token/expiry; this adds the SET scope so the accept flow
/// can mint the right <see cref="GalleryGrant"/>.
/// </para>
/// </summary>
[HostScoped]
public sealed class GalleryInvite : Entity<GalleryInvite>
{
    /// <summary>FK to the control-plane <c>Invite.Id</c> — the opaque accept token lives there.</summary>
    public string InviteId { get; set; } = "";

    /// <summary>The shared set (an <see cref="Event"/> id).</summary>
    public string EventId { get; set; } = "";

    /// <summary>The studio (a TenantRecord id) the set belongs to.</summary>
    public string StudioTenantId { get; set; } = "";

    /// <summary>The guest's permission template on accept (e.g. "viewer"). Maps to <see cref="GalleryGrant.Permissions"/>.</summary>
    public string GuestRole { get; set; } = "viewer";

    /// <summary>The studio operator (identity id) who issued the invite.</summary>
    public string? InvitedBy { get; set; }

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Deterministic id — one gallery-invite per control-plane invite.</summary>
    public static string KeyFor(string inviteId) => DeterministicId.From("gallery-invite", inviteId);
}
