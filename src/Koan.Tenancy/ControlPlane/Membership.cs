using System;
using System.Collections.Generic;
using System.Linq;
using Koan.Core;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Tenancy;

/// <summary>
/// The bridge between a subject and a tenant — <b>roles live here, on the membership</b>
/// (the StackExchange model: one identity, N tenants, a role per tenant), never global. A dogfooded
/// <c>[HostScoped]</c> control-plane row, queried by <see cref="TenantId"/>. Roles grant authority only inside that
/// tenant; there is no tenant-zero or implicit owner. Identity lifecycle enforcement is supplied by the optional
/// Identity Tenancy bridge.
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

    /// <summary>
    /// The deterministic id for a <c>(tenantId, identityId)</c> seat — one membership per subject per tenant. Repeated
    /// grants converge to one row. Creation sites set this id; queries stay by field.
    /// </summary>
    public static string KeyFor(string tenantId, string identityId) => DeterministicId.From(tenantId, identityId);
}
