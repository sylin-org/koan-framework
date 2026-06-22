using System;
using System.Security.Cryptography;
using System.Text;

namespace Koan.Tenancy;

/// <summary>
/// The dev auto-seed (ARCH-0099 §1), pure and deterministic so it is unit-testable. In dev-open posture the
/// module seeds <b>one</b> in-memory tenant smart-named from the developer (<c>leo@acme.dev → "Acme"</c>, never
/// <c>"default"</c>), makes the loopback caller its <see cref="TenancyRoles.Owner"/> (role-on-membership, zero
/// cross-tenant reach), and mints a <b>branded, per-machine</b> signing key (<see cref="TenancyDevBrand"/>) so
/// the relaxation is self-announcing and a leak into prod is caught. Nothing here is created in Production (the
/// seed runs only under Open posture, and a forced-Open-in-prod boot is refused first).
/// </summary>
public sealed record TenancyDevSeed(
    string TenantId,
    string TenantName,
    string OwnerIdentityId,
    string OwnerRole,
    string SigningKey)
{
    /// <summary>The stable, immutable surrogate id of the seeded dev tenant.</summary>
    public const string DevTenantId = "dev";

    /// <summary>Compose the dev seed from the developer identity and machine name.</summary>
    public static TenancyDevSeed Create(string? userOrEmail, string machineName)
    {
        var owner = string.IsNullOrWhiteSpace(userOrEmail) ? "dev@localhost" : userOrEmail!.Trim();
        return new TenancyDevSeed(
            TenantId: DevTenantId,
            TenantName: DeriveTenantName(userOrEmail),
            OwnerIdentityId: owner,
            OwnerRole: TenancyRoles.Owner,
            SigningKey: MintKey(machineName, owner));
    }

    /// <summary>
    /// Smart-name the dev tenant: an email yields its second-level domain label (<c>leo@acme.dev → Acme</c>), a
    /// bare username is title-cased (<c>leo → Leo</c>), and nothing yields the friendly default <c>Acme</c> —
    /// never <c>default</c>.
    /// </summary>
    public static string DeriveTenantName(string? userOrEmail)
    {
        if (string.IsNullOrWhiteSpace(userOrEmail)) return "Acme";
        var s = userOrEmail.Trim();
        var at = s.IndexOf('@');
        if (at >= 0 && at < s.Length - 1)
        {
            var domain = s[(at + 1)..];
            var dot = domain.IndexOf('.');
            return Titlecase(dot > 0 ? domain[..dot] : domain);
        }
        return Titlecase(at > 0 ? s[..at] : s);
    }

    /// <summary>
    /// Mint a per-machine-stable, branded signing key (so dev tokens survive a restart, and the
    /// <see cref="TenancyDevBrand.Prefix"/> brand makes a leak into prod fail the boot pre-flight).
    /// </summary>
    public static string MintKey(string? machineName, string? owner)
    {
        var material = (machineName ?? "") + "|" + (owner ?? "");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return TenancyDevBrand.Prefix + Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    private static string Titlecase(string s)
    {
        s = s.Trim();
        return s.Length == 0 ? "Acme" : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
