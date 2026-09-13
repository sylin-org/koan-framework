namespace Koan.Identity.Roles;

/// <summary>
/// A monotonically increasing application-owned access-fact version and the scoped-role slice it can affect.
/// </summary>
public sealed record ScopedRoleDomainVersionChange(
    ScopedRoleScopeRef Scope,
    string VersionKey,
    long Version);
