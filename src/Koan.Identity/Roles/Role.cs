using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity.Roles;

/// <summary>
/// A server-owned role or group. <see cref="Key"/> is its stable authorization token; its row id is composed from the
/// key (<see cref="KeyFor"/>), so two keys that a store's collation would equate never share a row. Its members are
/// <see cref="RoleMember"/> rows.
/// </summary>
public sealed class Role : Entity<Role>, IAmbientExempt, IRequiresLifecycleEnforcement
{
    /// <summary>The stable authorization token: <c>role:member</c>, <c>group:gardeners</c>, <c>everyone</c>.</summary>
    public string Key { get; set; } = "";

    public string Name { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    [Timestamp] public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>The one row id of the role keyed <paramref name="roleKey"/>.</summary>
    public static string KeyFor(string roleKey) => RoleContract.ComposeId(nameof(Role), roleKey);

    /// <summary>Pure authorization: any requested criterion token must be present in the compiled bag.</summary>
    public static bool CanDo(PermissionCriteria criteria, RoleBag personBag)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(personBag);
        return personBag.ContainsAny(criteria.AnyOf);
    }
}
