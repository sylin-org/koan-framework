using Microsoft.AspNetCore.DataProtection;

namespace Koan.Identity.Mfa;

/// <summary>
/// SEC-0007 P3-grp4 — encrypts a TOTP shared secret at rest. A TOTP secret is recoverable (it must be decryptable to
/// compute the expected code), so unlike a password it cannot be hashed — it is encrypted. The default rides ASP.NET
/// <see cref="IDataProtectionProvider"/>; an app must persist its data-protection keys in production (the standard
/// guidance) so secrets survive a restart.
/// </summary>
public interface IMfaSecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedSecret);
}

/// <summary>The default <see cref="IMfaSecretProtector"/> over ASP.NET data protection.</summary>
internal sealed class DataProtectionMfaSecretProtector : IMfaSecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionMfaSecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Koan.Identity.Mfa.Secret.v1");

    public string Protect(string secret) => _protector.Protect(secret);
    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}
