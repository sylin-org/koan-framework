using System.Security.Claims;

namespace Koan.Identity.Credentials;

/// <summary>
/// SEC-0007 P3-grp4 — the standard <c>amr</c> (authentication methods reference) / <c>acr</c> (authentication
/// context class) claims a sign-in carries, and helpers to read/write them. <c>amr</c> records which factors were
/// proven this round (one claim per method: <c>pwd</c>, <c>otp</c>, <c>passkey</c>); the step-up gate reads it to
/// decide which requirements are satisfied, and the SEC-0004 floor can gate a sensitive action on a fresh strong
/// factor (Phase 2). The factors are the same whether they run at login or mid-session — one model, primary or step-up.
/// </summary>
public static class CredentialAuthClaims
{
    /// <summary>Authentication methods reference — one claim per proven method.</summary>
    public const string Amr = "amr";

    /// <summary>Authentication context class — the assurance level (<see cref="Aal1"/> / <see cref="Aal2"/>).</summary>
    public const string Acr = "acr";

    /// <summary>Single-factor.</summary>
    public const string Aal1 = "aal1";

    /// <summary>Multi-factor (a second, independent factor was proven).</summary>
    public const string Aal2 = "aal2";

    // Canonical amr method values.
    public const string Password = "pwd";
    public const string Totp = "otp";
    public const string Passkey = "passkey";

    /// <summary>A redeemed account-recovery code — the documented lockout safety net; satisfies the MFA step-up so a lost authenticator doesn't lock the person out.</summary>
    public const string Recovery = "recovery";

    /// <summary>The set of <c>amr</c> methods proven on this principal.</summary>
    public static IReadOnlySet<string> MethodsOf(ClaimsPrincipal principal)
        => principal.FindAll(Amr).Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.Ordinal);

    /// <summary>True when this principal proved at least two distinct factors (multi-factor).</summary>
    public static bool IsMultiFactor(ClaimsPrincipal principal) => MethodsOf(principal).Count >= 2;

    /// <summary>Add the <c>amr</c> claims for <paramref name="methods"/> (deduped) + the derived <c>acr</c> level onto <paramref name="identity"/>.</summary>
    public static void Stamp(ClaimsIdentity identity, params string[] methods)
    {
        var existing = identity.FindAll(Amr).Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var m in methods)
            if (!string.IsNullOrWhiteSpace(m) && existing.Add(m))
                identity.AddClaim(new Claim(Amr, m));

        var acr = existing.Count >= 2 ? Aal2 : Aal1;
        if (identity.FindFirst(Acr) is { } prior) identity.RemoveClaim(prior);
        identity.AddClaim(new Claim(Acr, acr));
    }
}
