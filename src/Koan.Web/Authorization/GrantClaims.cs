using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0005 — maps an <see cref="AgentGrant.Capability"/> term (the <c>[Access]</c> vocabulary) to the principal
/// claim(s) that satisfy the matching gate bag, and enriches a principal with the claims a set of granted
/// capabilities confers — on a dedicated carrier identity, so the original identity/auth is preserved (mirroring
/// <see cref="OriginStamp"/>). Only <c>is:</c>/<c>has:</c> terms are grantable; <c>owner</c>/<c>origin</c>/
/// <c>anyone</c>/<c>authenticated</c> map to nothing (they are not capabilities an agent can be lent).
/// </summary>
public static class GrantClaims
{
    /// <summary>The claim(s) a capability term confers, or none for an ungrantable/unknown term.</summary>
    public static IEnumerable<Claim> For(string capability)
    {
        var t = (capability ?? string.Empty).Trim();
        if (t.StartsWith("is:", StringComparison.OrdinalIgnoreCase)) return One(ClaimTypes.Role, t.Substring(3));
        if (t.StartsWith("has:role:", StringComparison.OrdinalIgnoreCase)) return One(ClaimTypes.Role, t.Substring(9));
        if (t.StartsWith("has:scope:", StringComparison.OrdinalIgnoreCase)) return One("scope", t.Substring(10));
        if (t.StartsWith("has:claim:", StringComparison.OrdinalIgnoreCase))
        {
            var kv = t.Substring(10);
            var eq = kv.IndexOf('=');
            if (eq > 0 && eq < kv.Length - 1) return One(kv.Substring(0, eq), kv.Substring(eq + 1));
        }
        return Array.Empty<Claim>();
    }

    private static Claim[] One(string type, string value)
    {
        value = value.Trim();
        return value.Length == 0 ? Array.Empty<Claim>() : new[] { new Claim(type.Trim(), value) };
    }

    /// <summary>Returns a principal enriched with the claims the granted <paramref name="capabilities"/> confer (on a
    /// carrier identity), or the original principal unchanged when nothing is grantable.</summary>
    public static ClaimsPrincipal Enrich(ClaimsPrincipal principal, IEnumerable<string> capabilities)
    {
        var claims = capabilities.SelectMany(For).ToList();
        if (claims.Count == 0) return principal;
        // The carrier is authenticated so role/scope claims resolve via IsInRole / FindAll; it is added LAST so the
        // caller's primary identity (and its IsAuthenticated) is unchanged.
        var carrier = new ClaimsIdentity(claims, authenticationType: "koan:grant");
        var identities = principal.Identities.ToList();
        identities.Add(carrier);
        return new ClaimsPrincipal(identities);
    }
}
