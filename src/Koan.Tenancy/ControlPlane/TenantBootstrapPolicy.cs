using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Koan.Tenancy;

/// <summary>
/// Decides WHO may claim first-Owner (ARCH-0099 §2) — pure and testable. In <b>Development</b> anyone (the
/// loopback caller) may claim: that is the dev auto-seed. In <b>Production</b> the claim window is gated behind a
/// pre-seeded allowlist (<c>Koan:Tenancy:BootstrapAdminEmails</c>) <b>or</b> a one-time bootstrap token printed
/// to the host log (a host-only CLI seed is the zero-web-attack-surface escape hatch). Token comparison is
/// constant-time so the gate is not a timing oracle.
/// </summary>
public static class TenantBootstrapPolicy
{
    /// <summary>True when <paramref name="identityId"/> is permitted to claim first-Owner in the current environment.</summary>
    public static bool CanClaim(
        bool isDevelopment,
        string? identityId,
        IReadOnlyCollection<string>? allowlist,
        string? providedToken,
        string? expectedToken)
    {
        if (isDevelopment) return true;

        if (!string.IsNullOrWhiteSpace(identityId) && allowlist is not null &&
            allowlist.Any(e => string.Equals(e, identityId, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!string.IsNullOrEmpty(expectedToken) && FixedTimeEquals(providedToken, expectedToken))
            return true;

        return false;
    }

    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null) return false;
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return ba.Length == bb.Length && CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
