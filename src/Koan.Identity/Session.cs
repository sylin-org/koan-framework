using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 Layer 1 — the durable twin of a sign-in session (the cookie stays the transport; this is the device-list
/// + revocation record). One per device/browser. Revoking a session is immediate and observable; "sign out
/// everywhere-else" revokes every session for a person except the current one and bumps the owner's
/// <see cref="Identity.Epoch"/> so server-side tokens minted earlier are invalidated too.
/// </summary>
public sealed class Session : Entity<Session>, IAmbientExempt
{
    /// <summary>The owning person.</summary>
    [Parent(typeof(Identity))]
    public string IdentityId { get; set; } = "";

    /// <summary>Device label (e.g. "MacBook Pro", "iPhone").</summary>
    public string? Device { get; set; }

    /// <summary>Browser/user-agent family.</summary>
    public string? Browser { get; set; }

    /// <summary>Operating system family.</summary>
    public string? Os { get; set; }

    /// <summary>Approximate city derived from the sign-in IP (coarse, privacy-preserving).</summary>
    public string? ApproxCity { get; set; }

    /// <summary>True once the session has been revoked (sign-out, "everywhere-else", or admin action).</summary>
    public bool Revoked { get; set; }

    /// <summary>When the session was revoked, if it was.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>First seen (set once, on creation).</summary>
    [Timestamp]
    public DateTimeOffset FirstSeen { get; set; }

    /// <summary>Last activity (updated on every save).</summary>
    [Timestamp(OnSave = true)]
    public DateTimeOffset LastActive { get; set; }

    /// <summary>True while the session may be used.</summary>
    public bool IsActive => !Revoked;
}
