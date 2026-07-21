using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity.Impersonation;

/// <summary>
/// SEC-0007 D8 / Layer 3 — a request to impersonate a person, with dual-control. An actor REQUESTS (mandatory
/// reason + ticket); a different approver APPROVES (no self-approval) and time-boxes it; the target (or an admin)
/// can revoke. It is a queryable, revocable entity — issue = approve, revoke = <c>Remove</c>/flag, expire =
/// <see cref="ExpiresAt"/>. "Who may impersonate whom" being an entity means it audits and explains for free.
/// </summary>
public sealed class ImpersonationGrant : Entity<ImpersonationGrant>, IAmbientExempt
{
    /// <summary>The subject who may impersonate (the operator).</summary>
    [Parent(typeof(Identity))]
    public string Actor { get; set; } = "";

    /// <summary>The subject being impersonated.</summary>
    public string Target { get; set; } = "";

    /// <summary>Mandatory justification (audited).</summary>
    public string Reason { get; set; } = "";

    /// <summary>Optional ticket reference.</summary>
    public string? Ticket { get; set; }

    /// <summary>The approver's subject — <see langword="null"/> while pending; must differ from <see cref="Actor"/> (no self-approval).</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>When it was approved.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>When the grant expires (set at approval — impersonation is always time-boxed).</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>True once revoked (by the target or an admin).</summary>
    public bool Revoked { get; set; }

    /// <summary>Set once, when requested.</summary>
    [Timestamp]
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>True once an approver (≠ actor) has approved.</summary>
    public bool IsApproved => !string.IsNullOrEmpty(ApprovedBy);

    /// <summary>True when approved, not revoked, and not expired as of <paramref name="now"/>.</summary>
    public bool IsActive(DateTimeOffset now)
        => IsApproved && !Revoked && (ExpiresAt is null || ExpiresAt.Value > now);
}
