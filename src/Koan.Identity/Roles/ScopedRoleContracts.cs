using Koan.Core;
using Koan.Data.Abstractions.Filtering;

namespace Koan.Identity.Roles;

public enum ScopedRoleStatus { Active, Disabled, Retired }
public enum ScopedRolePropagation { Local, Descendants }
public enum ScopedRoleOverrideMode { Inherit, Replace }
public enum ScopedRoleAudienceKind { Anonymous, Authenticated, Subject, Role }
public enum ScopedRoleConditionOperator { Equal, LessThanOrEqual, GreaterThanOrEqual }

/// <summary>Fixed semantic input bounds for scoped-role management contracts.</summary>
public static class ScopedRoleInputLimits
{
    public const int IdentifierLength = 256;
    public const int NameLength = 160;
    public const int DescriptionLength = 1024;
    public const int PresentationEntries = 32;
    public const int PresentationKeyLength = 64;
    public const int PresentationValueLength = 512;
    public const int Parameters = 32;
    public const int ParameterNameLength = 128;
    public const int ParameterValueLength = 512;
}

public enum ScopedRoleAuthorityOperation
{
    RegisterScope = 0,
    DefineRole = 1,
    EditRole = 2,
    AssignRole = 3,
    RevokeRole = 4,
    ManagePolicy = 5,
    Preview = 6,
    ReadAudit = 7,
    ReadDefinitions = 8,
    ReadAssignments = 9,
    ReadPolicies = 10,
    ResetPolicy = 11,
}

/// <summary>A trusted, tenant-bound location in an application's one-parent scope tree.</summary>
public sealed record ScopedRoleScopeRef(string TenantId, string Type, string Id)
{
    public ScopedRoleScopeRef Normalize() => new(
        Require(TenantId, nameof(TenantId)), Require(Type, nameof(Type)), Require(Id, nameof(Id)));

    public override string ToString() => $"{TenantId}:{Type}/{Id}";

    internal static string Require(string value, string name)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException($"{name} is required.", name);
}

/// <summary>The stable subject used by the engine; credentials may change without changing this identity.</summary>
public sealed record ScopedRoleActor(string Subject, bool IsAuthenticated = true)
{
    public static ScopedRoleActor Anonymous { get; } = new("anonymous", false);
    internal string StableSubject => IsAuthenticated ? ScopedRoleScopeRef.Require(Subject, nameof(Subject)) : "anonymous";
}

/// <summary>Resolves the effective subject for current-subject checks; distinct from real-actor audit attribution.</summary>
public interface IScopedRoleSubjectAccessor
{
    string? CurrentSubject { get; }
}

public sealed record ScopedRoleCondition(
    string Parameter,
    ScopedRoleConditionOperator Operator,
    string Value);

/// <summary>One correlated grant path. Conditions are a conjunction and are never scalar-maximized.</summary>
public sealed record ScopedRoleGrantClause(
    string Capability,
    IReadOnlyList<ScopedRoleCondition>? Conditions = null);

/// <summary>One correlated audience alternative in an explicit replacement.</summary>
public sealed record ScopedRoleAudienceClause(
    ScopedRoleAudienceKind Kind,
    string? Value = null,
    IReadOnlyList<ScopedRoleCondition>? Conditions = null);

public sealed record ScopedRoleCapabilityDescriptor(
    string Key,
    IReadOnlySet<string> ScopeTypes,
    string? Label = null,
    string? Description = null,
    bool AllowsAnonymous = false,
    IReadOnlySet<string>? Parameters = null);

public sealed record ScopedRoleScopeDescriptor(string Type, string? ParentType = null);

public static class ScopedRoleResourceActions
{
    public const string Read = "read";
    public const string Create = "create";
    public const string Update = "update";
    public const string Delete = "delete";
}

public sealed record ScopedRoleResourceDescriptor(
    string Entity,
    string ScopeType,
    string ScopeField,
    IReadOnlyDictionary<string, string> Actions)
{
    /// <summary>The direct tenant-key field that is conjoined with <see cref="ScopeField"/> for every provider query.</summary>
    public string? TenantField { get; init; }
}

/// <summary>Application-owned structural vocabulary compiled when Identity composes.</summary>
[KoanDiscoverable]
public interface IScopedRoleCatalogContributor
{
    void Describe(ScopedRoleCatalogBuilder catalog);
}

public sealed record ScopedRoleAuthorityRequest(
    ScopedRoleActor Actor,
    ScopedRoleAuthorityOperation Operation,
    ScopedRoleScopeRef Target,
    string? Subject = null,
    string? RoleId = null,
    ScopedRolePropagation? Propagation = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlySet<string>? EffectiveCapabilities = null,
    IReadOnlySet<string>? EffectiveRoleIds = null)
{
    public IReadOnlyList<ScopedRoleGrantClause>? EffectiveGrants { get; init; }
    public IReadOnlyList<ScopedRoleAudienceClause>? EffectiveAudience { get; init; }
}

/// <summary>One complete delegation envelope. A mutation must fit one envelope; envelopes are not unioned.</summary>
public sealed record ScopedRoleAuthorityEnvelope(
    ScopedRoleScopeRef Scope,
    IReadOnlySet<ScopedRoleAuthorityOperation> Operations,
    bool Descendants = false,
    IReadOnlySet<string>? RoleIds = null,
    IReadOnlySet<string>? Capabilities = null,
    IReadOnlySet<ScopedRolePropagation>? Propagations = null,
    DateTimeOffset? MaximumExpiry = null,
    bool AllowSelfAssignment = false,
    string? ProofKey = null,
    long? ProofVersion = null)
{
    public IReadOnlyList<ScopedRoleGrantClause>? GrantClauses { get; init; }
    public IReadOnlyList<ScopedRoleAudienceClause>? AudienceClauses { get; init; }
}

[KoanDiscoverable]
public interface IScopedRoleAuthorityContributor
{
    ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(
        ScopedRoleAuthorityRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Revalidates the selected versioned envelope at the mutation's lifecycle boundary. Implementations must read
    /// their authoritative provider state; an in-process cache or earlier boolean is not a commit proof.
    /// </summary>
    ValueTask<bool> Validate(
        ScopedRoleAuthorityRequest request,
        ScopedRoleAuthorityEnvelope envelope,
        CancellationToken ct = default);
}

public sealed record ScopedRoleGuardRequest(
    ScopedRoleActor Subject,
    ScopedRoleScopeRef Target,
    string Capability,
    IReadOnlyDictionary<string, object?> Parameters);

public sealed record ScopedRoleGuardResult(
    bool Allowed,
    string Code,
    string Message,
    string? VersionKey = null,
    long? Version = null)
{
    public static ScopedRoleGuardResult Permit(string code = "guard.permit", string message = "A mandatory guard permitted the action.")
        => new(true, code, message);
    public static ScopedRoleGuardResult Deny(string code, string message, string? versionKey = null, long? version = null)
        => new(false, code, message, versionKey, version);
}

/// <summary>Application-owned mandatory limits. Every contributor is intersected with ordinary role grants.</summary>
[KoanDiscoverable]
public interface IScopedRoleGuardContributor
{
    ValueTask<ScopedRoleGuardResult> Evaluate(ScopedRoleGuardRequest request, CancellationToken ct = default);
}

public sealed record RegisterScopedRoleScope(
    ScopedRoleScopeRef Scope,
    ScopedRoleScopeRef? Parent = null,
    long? ExpectedVersion = null);

public sealed record DefineScopedRole(
    ScopedRoleScopeRef Scope,
    string Name,
    IReadOnlyList<ScopedRoleGrantClause> Grants,
    string? Id = null,
    string? Purpose = null,
    IReadOnlyDictionary<string, string>? Presentation = null);

public sealed record EditScopedRole(
    string RoleId,
    long ExpectedVersion,
    string? Name = null,
    string? Purpose = null,
    IReadOnlyList<ScopedRoleGrantClause>? Grants = null,
    IReadOnlyDictionary<string, string>? Presentation = null,
    ScopedRoleStatus? Status = null);

public sealed record AssignScopedRole(
    ScopedRoleScopeRef Scope,
    string Subject,
    string RoleId,
    ScopedRolePropagation Propagation = ScopedRolePropagation.Local,
    DateTimeOffset? ExpiresAt = null);

/// <summary>Names one member of a scoped role collection. Removing an absent member is a successful no-op.</summary>
public sealed record RemoveScopedRoleMembership(
    ScopedRoleScopeRef Scope,
    string Subject,
    string RoleId,
    long? ExpectedVersion = null);

public sealed record ReplaceScopedRolePolicy(
    ScopedRoleScopeRef Scope,
    string Capability,
    IReadOnlyList<ScopedRoleAudienceClause> Audience,
    long? ExpectedVersion = null);

public sealed record ScopedRoleReason(string Code, string Message, string? SourceType = null, string? SourceId = null);

/// <summary>The immutable result shared by checks, explanations, previews and later enforcement adapters.</summary>
public sealed record ScopedRolePlan(
    ScopedRoleActor Subject,
    ScopedRoleScopeRef Target,
    string Capability,
    bool Allowed,
    IReadOnlyList<ScopedRoleReason> Reasons,
    IReadOnlyList<string> RoleIds,
    string? WinningPolicyId,
    IReadOnlyDictionary<string, long> Versions,
    DateTimeOffset EvaluatedAt)
{
    /// <summary>Compiled role/group memberships plus derived permission tokens for this subject and scope.</summary>
    public ScopedRoleMembershipSet Memberships { get; init; } = ScopedRoleMembershipSet.Empty;

    /// <summary>The compiled capability audience. Matching it does not include mandatory application guards.</summary>
    public ScopedRoleAudience Audience { get; init; } = ScopedRoleAudience.None;
}

public sealed record ScopedRolePreview(ScopedRolePlan Plan, bool IsSimulation = false);

public sealed record ScopedRoleEngineLimits(
    int MaxAncestryDepth,
    int MaxBindingsPerSubject,
    int MaxPoliciesPerTenant,
    int MaxClausesPerRecord)
{
    public int MaxDirectoryPageSize { get; init; } = 100;
    public int MaxBindingsPerScope { get; init; } = 1024;
    public int MaxCompiledSnapshots { get; init; } = 1024;
    public int MaxDomainVersions { get; init; } = 4096;
}

public sealed record ScopedRolePage<TEntity>(
    IReadOnlyList<TEntity> Items,
    long TotalCount,
    int Page,
    int PageSize);

/// <summary>Safe structural vocabulary for headless management clients and agents.</summary>
public sealed record ScopedRoleEngineDescriptor(
    IReadOnlyList<ScopedRoleScopeDescriptor> Scopes,
    IReadOnlyList<ScopedRoleCapabilityDescriptor> Capabilities,
    IReadOnlyList<ScopedRoleResourceDescriptor> Resources,
    ScopedRoleEngineLimits Limits);

public sealed record ScopedRoleQueryPlan(ScopedRolePlan Access, Filter Constraint);

public sealed class RoleEngineOptions
{
    public const string SectionPath = "Koan:Identity:ScopedRoles";
    public int MaxAncestryDepth { get; set; } = 16;
    public int MaxBindingsPerSubject { get; set; } = 256;
    public int MaxBindingsPerScope { get; set; } = 1024;
    public int MaxPoliciesPerTenant { get; set; } = 512;
    public int MaxClausesPerRecord { get; set; } = 64;
    public int MaxDirectoryPageSize { get; set; } = 100;
    public int MaxCompiledSnapshots { get; set; } = 1024;
    public int MaxDomainVersions { get; set; } = 4096;
}
