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
}
