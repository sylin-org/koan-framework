namespace Koan.Identity.Mfa;

/// <summary>
/// SEC-0007 P3-grp4 — MFA brute-force protection (bound from <c>Koan:Identity:Mfa</c>). A 6-digit TOTP has only a
/// million combinations, so an online attempt cap is load-bearing (NIST 800-63B). This is the framework's in-factor
/// defense; an app should ALSO place a request rate-limiter in front of the factor-challenge endpoints (and the
/// recovery-code redeem path) for the network-level dimension.
/// </summary>
public sealed class MfaOptions
{
    public const string SectionPath = "Koan:Identity:Mfa";

    /// <summary>Consecutive failed TOTP verifications before a temporary lockout. Default 5.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>How long the factor is locked after <see cref="MaxFailedAttempts"/> consecutive failures. Default 5 minutes.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(5);
}
