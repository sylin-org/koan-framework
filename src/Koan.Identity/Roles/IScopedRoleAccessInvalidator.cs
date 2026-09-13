namespace Koan.Identity.Roles;

/// <summary>
/// Invalidates compiled scoped-role access after an application-owned domain fact commits.
/// Multi-process applications must deliver the same version change to every process.
/// </summary>
public interface IScopedRoleAccessInvalidator
{
    void Invalidate(ScopedRoleDomainVersionChange change);
}
