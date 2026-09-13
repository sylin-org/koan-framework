using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Identity.Roles;

/// <summary>The internal persisted role/group collection for one subject at one exact scope.</summary>
internal sealed class ScopedRoleParticipant : Entity<ScopedRoleParticipant>, IAmbientExempt, IRequiresLifecycleEnforcement
{
    public string TenantId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string ScopeType { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public List<string> Roles { get; set; } = [];
    public List<string> Groups { get; set; } = [];
    [Timestamp] public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";

    public ScopedRoleScopeRef Scope() => new(TenantId, ScopeType, ScopeId);

    public static string KeyFor(ScopedRoleScopeRef scope, string subject)
        => DeterministicId.From(scope.TenantId, scope.Type, scope.Id, subject);

    public bool Contains(string roleId) => Collection(roleId).Contains(roleId, StringComparer.Ordinal);

    public bool Add(string roleId)
    {
        var collection = Collection(roleId);
        if (collection.Contains(roleId, StringComparer.Ordinal)) return false;
        collection.Add(roleId);
        collection.Sort(StringComparer.Ordinal);
        return true;
    }

    public bool Remove(string roleId) => Collection(roleId).Remove(roleId);

    public bool IsEmpty => Roles.Count == 0 && Groups.Count == 0;

    public IEnumerable<string> RoleIds => Roles.Concat(Groups);

    private List<string> Collection(string roleId)
        => roleId.StartsWith("group:", StringComparison.Ordinal) ? Groups : Roles;
}
