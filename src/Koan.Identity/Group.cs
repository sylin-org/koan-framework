using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 Layer 1 — an org-free grouping of identities for bulk role assignment. Nestable via a self
/// <c>[Parent]</c> (maps to IdP groups later via SCIM). Membership is a simple id list in P0/P1; a dedicated
/// membership entity can replace it if a group grows large.
/// </summary>
public sealed class Group : Entity<Group>, IAmbientExempt
{
    /// <summary>Group display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Optional parent group (nestable).</summary>
    [Parent(typeof(Group))]
    public string? ParentGroupId { get; set; }

    /// <summary>Optional description.</summary>
    public string? Description { get; set; }

    /// <summary>The identities that belong to this group.</summary>
    public List<string> MemberIdentityIds { get; set; } = new();

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }
}
