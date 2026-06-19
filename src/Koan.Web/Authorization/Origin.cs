using System;
using System.Linq;
using System.Security.Claims;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0004 — the transport-trust tier a call arrived on. This is WHERE the call came from, distinct from WHO the
/// caller is (identity): a STDIO call is <see cref="Local"/> yet anonymous. The framework stamps it as a
/// server-trusted <c>koan:origin</c> claim at the transport edge (see <see cref="OriginStamp"/>); the
/// <c>[Access(... "origin:local")]</c> gate term resolves against it.
/// </summary>
public enum OriginTier
{
    /// <summary>Same-process, OS-guaranteed — STDIO. The strongest signal; never inferable from a network call.</summary>
    Local,

    /// <summary>A network the app EXPLICITLY declares as trusted (the homelab/LAN case). Fail-closed: absent that
    /// declaration (<see cref="OriginOptions.InternalNetworks"/>) it never matches, so it can't be spoofed.</summary>
    Internal,

    /// <summary>Anything that crossed the perimeter — the safe default for any HTTP/networked caller.</summary>
    Remote,
}

/// <summary>
/// SEC-0004 — the <c>koan:origin</c> claim vocabulary. The claim is FRAMEWORK-STAMPED and server-trusted: the
/// transport edge strips any client-supplied value and writes the real tier (mirroring the create owner-stamp), so
/// it cannot be forged. <c>origin:local</c> in an <c>[Access]</c> value is authoring sugar for
/// <c>has:claim:koan:origin=local</c>, riding the existing claim machinery + the Phase 3.3 principal thread.
/// </summary>
public static class Origin
{
    /// <summary>The server-trusted claim type carrying the transport tier.</summary>
    public const string ClaimType = "koan:origin";

    public const string Local = "local";
    public const string Internal = "internal";
    public const string Remote = "remote";

    /// <summary>The canonical claim value for a tier.</summary>
    public static string Value(OriginTier tier) => tier switch
    {
        OriginTier.Local => Local,
        OriginTier.Internal => Internal,
        _ => Remote,
    };
}

/// <summary>
/// SEC-0004 — stamps the server-trusted <see cref="Origin.ClaimType"/> claim onto a principal at the transport
/// edge. ALWAYS strips any pre-existing (potentially client-forged) <c>koan:origin</c> claim first, then adds the
/// framework's value on a dedicated UNAUTHENTICATED carrier identity — so it never changes
/// <see cref="ClaimsPrincipal.Identity"/>/<c>IsAuthenticated</c> (a STDIO caller stays anonymous while gaining
/// <c>origin:local</c>). Pure and allocation-light; called once per request at the edge.
/// </summary>
public static class OriginStamp
{
    public static ClaimsPrincipal Apply(ClaimsPrincipal principal, OriginTier tier)
    {
        if (principal is null) throw new ArgumentNullException(nameof(principal));

        // Copy every existing identity MINUS any koan:origin claim (defeat a forged value in the caller's token).
        var identities = principal.Identities
            .Select(id => (ClaimsIdentity)new ClaimsIdentity(
                id.Claims.Where(c => !string.Equals(c.Type, Origin.ClaimType, StringComparison.Ordinal)),
                id.AuthenticationType, id.NameClaimType, id.RoleClaimType))
            .ToList();

        // The framework's tier rides on a distinct, unauthenticated carrier identity (added last → not primary).
        var carrier = new ClaimsIdentity();
        carrier.AddClaim(new Claim(Origin.ClaimType, Origin.Value(tier)));
        identities.Add(carrier);

        return new ClaimsPrincipal(identities);
    }

    /// <summary>True when a principal already carries a framework origin stamp (used by the request builder to avoid
    /// double-stamping a pre-stamped non-HTTP principal and to detect an unstamped path).</summary>
    public static bool IsStamped(ClaimsPrincipal principal)
        => principal is not null && principal.HasClaim(c => string.Equals(c.Type, Origin.ClaimType, StringComparison.Ordinal));
}
