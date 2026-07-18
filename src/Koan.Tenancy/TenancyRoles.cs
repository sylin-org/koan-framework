using System;

namespace Koan.Tenancy;

/// <summary>
/// Well-known tenancy roles. Tenant roles live on <see cref="Membership"/>, never on the global subject.
/// </summary>
public static class TenancyRoles
{
    /// <summary>The tenant owner role.</summary>
    public const string Owner = "koan:owner";

    /// <summary>The conventional tenant member role used when no narrower business role is supplied.</summary>
    public const string Member = "member";

    /// <summary>
    /// The host-face fleet operator / service-owner (ARCH-0104) — authority over the tenant <i>fleet</i> (registry,
    /// memberships, audit) from the control-plane console. It is a <b>global/host</b> role granted out-of-band and is
    /// <b>never derived from a tenant membership</b>: this is the design's "no master backdoor" — there is no
    /// tenant-zero, and the <c>IsDefault</c> tenant has zero special powers. Prod requires an explicit grant and
    /// fails closed; dev-open posture authorizes the local development surface without a durable grant.
    /// </summary>
    public const string Operator = "koan:tenancy-operator";

    /// <summary>
    /// True when <paramref name="role"/> is a <b>host</b> role that must never be grantable through a tenant
    /// membership. The administration path validates this and the request projection strips it again at the
    /// authorization chokepoint.
    /// </summary>
    public static bool IsReservedHostRole(string? role)
        => string.Equals(role?.Trim(), Operator, StringComparison.OrdinalIgnoreCase);
}
