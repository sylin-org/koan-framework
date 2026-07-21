using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 D3 / Layer 2 — the GLOBAL user↔role binding (the no-tenancy case) over standard .NET role keys.
/// Per-tenant binding stays <c>Membership.Roles</c>; one vocabulary is bound globally or per membership.
/// A deterministic id keeps a re-grant idempotent.
/// </summary>
public sealed class IdentityRole : Entity<IdentityRole>, IAmbientExempt
{
    /// <summary>The person this global role is bound to.</summary>
    [Parent(typeof(Identity))]
    public string IdentityId { get; set; } = "";

    /// <summary>The role key from the catalog (e.g. <c>koan:admin</c>).</summary>
    public string RoleKey { get; set; } = "";

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The deterministic id for an (identityId, roleKey) pair — keeps granting idempotent.</summary>
    public static string KeyFor(string identityId, string roleKey) => DeterministicId.From(identityId, roleKey);
}
