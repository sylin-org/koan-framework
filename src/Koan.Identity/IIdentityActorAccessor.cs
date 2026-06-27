namespace Koan.Identity;

/// <summary>
/// Resolves the subject id of the actor performing the current operation, for audit attribution. The core ships no
/// implementation (audit Actor is then null); a web/principal-aware layer (or P3 impersonation, which supplies the
/// <c>actor</c> claim) registers one. Read best-effort from the ambient provider inside lifecycle hooks.
/// </summary>
public interface IIdentityActorAccessor
{
    /// <summary>The acting subject (impersonator's <c>actor</c> claim if impersonating, else the principal's sub), or null.</summary>
    string? CurrentActorSubject { get; }
}
