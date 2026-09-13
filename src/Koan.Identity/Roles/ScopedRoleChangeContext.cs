namespace Koan.Identity.Roles;

public sealed record ScopedRoleChangeContext(
    string TenantId,
    ScopedRoleScopeRef Scope,
    string RoleId,
    string? Subject,
    ScopedRoleActor Actor,
    IReadOnlyList<ScopedRoleGrantClause> PreviousPermissions,
    IReadOnlyList<ScopedRoleGrantClause> CurrentPermissions,
    DateTimeOffset Timestamp,
    long Version,
    ScopedRoleChangePhase Phase,
    CancellationToken CancellationToken);
