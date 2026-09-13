using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace Koan.Identity.Roles;

/// <summary>A trusted persisted edge in the v1 one-parent scope hierarchy.</summary>
public sealed class ScopedRoleScope : Entity<ScopedRoleScope>, IAmbientExempt, IRequiresLifecycleEnforcement
{
    public string TenantId { get; set; } = "";
    public string Type { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public string? ParentType { get; set; }
    public string? ParentScopeId { get; set; }
    public long Version { get; set; } = 1;
    [Timestamp] public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";

    public static string KeyFor(ScopedRoleScopeRef scope) => DeterministicId.From(scope.TenantId, scope.Type, scope.Id);
    public ScopedRoleScopeRef Reference() => new(TenantId, Type, ScopeId);
}

/// <summary>An editable scoped role. AuthorityVersion changes only when effective authority can change.</summary>
public sealed class ScopedRoleDefinition : Entity<ScopedRoleDefinition>, IAmbientExempt, IRequiresLifecycleEnforcement
{
    public string TenantId { get; set; } = "";
    public string ScopeType { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Purpose { get; set; }
    public ScopedRoleStatus Status { get; set; } = ScopedRoleStatus.Active;
    public List<ScopedRoleGrantClause> Grants { get; set; } = [];
    public Dictionary<string, string> Presentation { get; set; } = new(StringComparer.Ordinal);
    public long Version { get; set; } = 1;
    public long AuthorityVersion { get; set; } = 1;
    [Timestamp] public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";

    public ScopedRoleScopeRef Scope() => new(TenantId, ScopeType, ScopeId);
}

/// <summary>A revocable scoped subject-to-role assignment approved against one authority version.</summary>
public sealed class ScopedRoleBinding : Entity<ScopedRoleBinding>, IAmbientExempt, IRequiresLifecycleEnforcement
{
    public string TenantId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string ScopeType { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public ScopedRolePropagation Propagation { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool Revoked { get; set; }
    public long ApprovedRoleVersion { get; set; }
    public Dictionary<string, long> ApprovedPolicyVersions { get; set; } = new(StringComparer.Ordinal);
    public long Version { get; set; } = 1;
    [Timestamp] public DateTimeOffset UpdatedAt { get; set; }
    public string IssuedBy { get; set; } = "";
    public string UpdatedBy { get; set; } = "";

    public ScopedRoleScopeRef Scope() => new(TenantId, ScopeType, ScopeId);
    public static string KeyFor(string tenantId, string subject, string roleId, ScopedRoleScopeRef scope)
        => DeterministicId.From(tenantId, subject, roleId, scope.Type, scope.Id);
}

/// <summary>The action policy at one scope. Replace owns the complete ordinary audience; Inherit owns none.</summary>
public sealed class ScopedRolePolicy : Entity<ScopedRolePolicy>, IAmbientExempt, IRequiresLifecycleEnforcement
{
    public string TenantId { get; set; } = "";
    public string ScopeType { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public string Capability { get; set; } = "";
    public ScopedRoleOverrideMode Mode { get; set; }
    public List<ScopedRoleAudienceClause> Audience { get; set; } = [];
    public long Version { get; set; } = 1;
    [Timestamp] public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";

    public ScopedRoleScopeRef Scope() => new(TenantId, ScopeType, ScopeId);
    public static string KeyFor(ScopedRoleScopeRef scope, string capability)
        => DeterministicId.From(scope.TenantId, scope.Type, scope.Id, capability);
}
