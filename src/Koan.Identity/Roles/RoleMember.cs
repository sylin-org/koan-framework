using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Identity.Roles;

/// <summary>
/// One person's membership of one role. A role's members are rows, so a role holds any number of them and adding or
/// removing one writes one row. Its id is composed from the role and the person (<see cref="KeyFor"/>), so adding the
/// same member twice writes the same row. It is read by person (the roles one person holds) and by role then person
/// (a role's members, a page at a time). <see cref="RoleCollection"/> is its only mutation owner.
/// </summary>
public sealed class RoleMember : Entity<RoleMember>, IAmbientExempt, IRequiresLifecycleEnforcement
{
    private const string RolePersonIndex = "ix_role_members_role_person";
    private const string PersonIndex = "ix_role_members_person";

    /// <summary>The role's key.</summary>
    [Index(Group = RolePersonIndex, Order = 0)]
    public string RoleKey { get; set; } = "";

    /// <summary>The person.</summary>
    [Index(Group = PersonIndex)]
    [Index(Group = RolePersonIndex, Order = 1)]
    public string PersonId { get; set; } = "";

    /// <summary>When the person was added to the role.</summary>
    public DateTimeOffset AddedAt { get; set; }

    /// <summary>Who added them, when an actor was known.</summary>
    public string? AddedBy { get; set; }

    /// <summary>The one id of <paramref name="personId"/>'s membership of <paramref name="roleKey"/>.</summary>
    public static string KeyFor(string roleKey, string personId)
        => RoleContract.ComposeId(nameof(RoleMember), roleKey, personId);
}
