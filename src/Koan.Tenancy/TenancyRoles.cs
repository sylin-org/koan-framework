using System;

namespace Koan.Tenancy;

/// <summary>
/// Well-known tenancy roles (ARCH-0099 §2). Roles are <b>on the membership</b> (the StackExchange model — one
/// identity, N tenants, a role per tenant), never global. <see cref="Owner"/> is the first-user-becomes-owner
/// role — full authority over <i>their own</i> tenant, zero cross-tenant reach (not the rejected tenant-zero
/// backdoor).
/// </summary>
public static class TenancyRoles
{
    /// <summary>The tenant owner — full authority over their own tenant, granted to the first user (dev: the auto-seeded loopback caller).</summary>
    public const string Owner = "koan:owner";

    /// <summary>
    /// The host-face fleet operator / service-owner (ARCH-0104) — authority over the tenant <i>fleet</i> (roster,
    /// lifecycle, audit) from the control-plane console. It is a <b>global/host</b> role granted out-of-band and is
    /// <b>never derived from a tenant membership</b>: this is the design's "no master backdoor" — there is no
    /// tenant-zero, and the <c>IsDefault</c> tenant has zero special powers. Prod requires an explicit grant and
    /// fails closed; dev-open posture may seed it for the loopback caller.
    /// </summary>
    public const string Operator = "koan:tenancy-operator";

    /// <summary>
    /// True when <paramref name="role"/> is a <b>host</b> role that must never be grantable through a tenant
    /// membership/invite (ARCH-0104 guardrail — "the operator role is never derived from a tenant membership").
    /// The invite path validates against this so an operator cannot mint fleet-operators by inviting a member with
    /// the operator role (a lateral privilege-escalation the tenant-resolution role-projection would otherwise honor).
    /// </summary>
    public static bool IsReservedHostRole(string? role)
        => string.Equals(role?.Trim(), Operator, StringComparison.OrdinalIgnoreCase);
}
