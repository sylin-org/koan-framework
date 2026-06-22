using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Tenancy;

/// <summary>
/// The bridge between a global identity and a tenant (ARCH-0099 §2) — <b>roles live here, on the membership</b>
/// (the StackExchange model: one identity, N tenants, a role per tenant), never global. A dogfooded
/// <c>[HostScoped]</c> control-plane row, queried by <see cref="TenantId"/>. The first user to claim a tenant
/// gets <see cref="TenancyRoles.Owner"/> — full authority over <i>their own</i> tenant, zero cross-tenant reach
/// (not the rejected tenant-zero backdoor).
/// </summary>
[HostScoped]
public sealed class Membership : Entity<Membership>
{
    /// <summary>The tenant this membership grants access to (a <see cref="TenantRecord"/> id).</summary>
    public string TenantId { get; set; } = "";

    /// <summary>The global identity (id or email) this membership is for.</summary>
    public string IdentityId { get; set; } = "";

    /// <summary>The roles granted within <see cref="TenantId"/> (e.g. <c>koan:owner</c>).</summary>
    public List<string> Roles { get; set; } = new();

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>True when this membership carries the owner role.</summary>
    public bool IsOwner => Roles.Contains(TenancyRoles.Owner);

    /// <summary>True when this membership carries <paramref name="role"/>.</summary>
    public bool HasRole(string role) => Roles.Contains(role);
}
