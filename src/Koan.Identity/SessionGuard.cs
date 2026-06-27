using System.Security.Claims;
using Koan.Data.Core;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 Layer 1 — the request-path enforcement that makes session revocation real (so it is not a write-only
/// flag nothing reads). A sign-in stamps a <see cref="Session"/> id claim onto the cookie; on each authenticated
/// request the auth flow handler's <c>OnValidatePrincipal</c> calls <see cref="ShouldRejectAsync"/> and rejects the
/// principal when its session has been revoked ("sign out everywhere-else") or its person is no longer active
/// (suspend / deactivate). This is what makes the ADR's "revocation is immediate + observable" true.
/// </summary>
internal static class SessionGuard
{
    /// <summary>The claim carrying the durable <see cref="Session"/> id on the cookie.</summary>
    public const string SessionClaim = "koan_sid";

    public static async Task<bool> ShouldRejectAsync(ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(subject)) return false;

        // Deprovisioning: a suspended/deactivated person cannot act. We reject only when we can CONFIRM a non-active
        // status — a transient missing read does not mass-sign-out (full deletion enforcement is the P4 receipt).
        var person = await Identity.Get(subject, ct).ConfigureAwait(false);
        if (person is not null && person.Status != IdentityStatus.Active) return true;

        // Sign out everywhere-else: a revoked (or vanished) session for this cookie is rejected.
        var sid = principal.FindFirst(SessionClaim)?.Value;
        if (!string.IsNullOrEmpty(sid))
        {
            var session = await Session.Get(sid, ct).ConfigureAwait(false);
            if (session is null || session.Revoked) return true;
        }

        return false;
    }
}
