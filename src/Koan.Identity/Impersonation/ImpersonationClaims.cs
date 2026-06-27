using System.Security.Claims;

namespace Koan.Identity.Impersonation;

/// <summary>
/// SEC-0007 D8 — the actor/subject claim model for safe impersonation. While impersonating, the principal's subject
/// is the TARGET and a distinct <c>actor</c> claim carries the operator — never a <c>sub</c> swap that erases who is
/// really acting. The banner header advertises the impersonation to the UI on every response.
/// </summary>
public static class ImpersonationClaims
{
    /// <summary>The claim carrying the real operator's subject while impersonating (distinct from <c>sub</c>).</summary>
    public const string ActorClaim = "koan_actor";

    /// <summary>Response header that advertises an active impersonation (the auto-injected banner's signal).</summary>
    public const string BannerHeader = "X-Koan-Impersonating";

    /// <summary>True when the principal is acting AS someone else (carries an <see cref="ActorClaim"/>).</summary>
    public static bool IsImpersonating(ClaimsPrincipal principal) => principal.FindFirst(ActorClaim) is not null;

    /// <summary>The real operator's subject while impersonating, or null.</summary>
    public static string? ActorOf(ClaimsPrincipal principal) => principal.FindFirst(ActorClaim)?.Value;

    /// <summary>
    /// Build the impersonated identity: subject = <paramref name="targetSubject"/> (so the session acts as them),
    /// plus an <see cref="ActorClaim"/> = <paramref name="actorSubject"/> and the target's effective roles. The
    /// sub is the target's; the actor claim preserves attribution (never a God-mode sub-swap).
    /// </summary>
    public static ClaimsIdentity BuildImpersonatedIdentity(string targetSubject, string actorSubject, IEnumerable<string> targetRoles)
    {
        var identity = new ClaimsIdentity("impersonation");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, targetSubject));
        identity.AddClaim(new Claim(ActorClaim, actorSubject));
        foreach (var role in targetRoles)
            if (!identity.HasClaim(ClaimTypes.Role, role))
                identity.AddClaim(new Claim(ClaimTypes.Role, role));
        return identity;
    }
}
