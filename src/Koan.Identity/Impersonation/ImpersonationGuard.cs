using System.Security.Claims;

namespace Koan.Identity.Impersonation;

/// <summary>
/// SEC-0007 D8 — the structural "cannot be God-mode" guard. While a principal carries an <c>actor</c> claim
/// (is impersonating), the dangerous identity verbs are refused (403). Impersonation lets you act AS the target,
/// never destroy/escalate as them — so account deletion, credential/MFA/passkey changes, role grants, token
/// issuance, data export, and nested impersonation are blocked by construction.
/// </summary>
public static class ImpersonationGuard
{
    /// <summary>
    /// The verbs that must never be performed under impersonation. This set equals what is actually ENFORCED on
    /// the request path (no aspirational labels) — each has a controller that 403s on it while impersonating.
    /// Group-4 verbs (credential/MFA/passkey change) join here when their endpoints land.
    /// </summary>
    public static readonly IReadOnlySet<string> DangerousActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "identity.delete",   // IdentityAdminController.Delete
        "identity.suspend",  // IdentityAdminController.Suspend
        "role.grant",        // IdentityAccessController.Grant
        "apitoken.issue",    // IdentitySelfServiceController.IssueToken
        "apitoken.rotate",   // IdentitySelfServiceController.RotateToken
        "impersonate.start", // ImpersonationController.Request (no nested impersonation)
    };

    /// <summary>True when <paramref name="action"/> must be refused because the principal is impersonating.</summary>
    public static bool IsBlocked(ClaimsPrincipal principal, string action)
        => ImpersonationClaims.IsImpersonating(principal) && DangerousActions.Contains(action);
}
