using System;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Tenancy;

/// <summary>
/// A pending invitation to join a tenant with a role (ARCH-0099 §2) — a dogfooded <c>[HostScoped]</c>
/// control-plane row. The opaque <see cref="Token"/> is the accept handle; accepting a redeemable invite creates
/// a <see cref="Membership"/> for the accepting identity. Invites carry their own expiry so a stale link cannot
/// grant access.
/// </summary>
[HostScoped]
public sealed class Invite : Entity<Invite>
{
    /// <summary>The tenant being joined (a <see cref="TenantRecord"/> id).</summary>
    public string TenantId { get; set; } = "";

    /// <summary>The invited email.</summary>
    public string Email { get; set; } = "";

    /// <summary>The role to grant on acceptance (e.g. <c>member</c>).</summary>
    public string Role { get; set; } = "";

    /// <summary>The opaque accept handle.</summary>
    public string Token { get; set; } = "";

    /// <summary>The current status; defaults to <see cref="InviteStatus.Pending"/>.</summary>
    public InviteStatus Status { get; set; } = InviteStatus.Pending;

    /// <summary>When the invite stops being redeemable.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>True when the invite is still pending and not past its expiry at <paramref name="now"/>.</summary>
    public bool IsRedeemable(DateTimeOffset now) => Status == InviteStatus.Pending && now < ExpiresAt;
}
