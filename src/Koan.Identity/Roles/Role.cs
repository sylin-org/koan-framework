using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity.Roles;

/// <summary>A server-owned role or group. Its id is the stable authorization token.</summary>
public sealed class Role : Entity<Role>, IAmbientExempt, IRequiresLifecycleEnforcement
{
    public string Name { get; set; } = "";
    public List<string> Permissions { get; set; } = [];
    public List<string> Members { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);
    [Timestamp] public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    /// <summary>Pure authorization: any requested criterion token must be present in the compiled bag.</summary>
    public static bool CanDo(PermissionCriteria criteria, RoleBag personBag)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(personBag);
        return personBag.ContainsAny(criteria.AnyOf);
    }
}
