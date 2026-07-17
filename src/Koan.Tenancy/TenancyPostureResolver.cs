namespace Koan.Tenancy;

/// <summary>
/// Resolves the <see cref="TenancyPosture"/> from environment detection (ARCH-0099 §1). Pure and deterministic
/// so the derivation is unit-testable without a host. An explicit <c>overridePosture</c> wins when
/// set — the sanctioned testing aid (e.g. exercising prod-closed locally); the dangerous case (forcing
/// <see cref="TenancyPosture.Open"/> while the environment is Production) is caught by the boot pre-flight, not
/// here. With no override, the ASP.NET rule verbatim: a positive <c>isDevelopment</c> ⇒
/// <see cref="TenancyPosture.Open"/>; everything else (Production, Staging, unset, ambiguous) ⇒
/// <see cref="TenancyPosture.Closed"/>.
/// </summary>
public static class TenancyPostureResolver
{
    /// <summary>Resolve the posture from the development flag and an optional explicit override.</summary>
    public static TenancyPosture Resolve(bool isDevelopment, TenancyPosture? overridePosture = null)
        => overridePosture ?? (isDevelopment ? TenancyPosture.Open : TenancyPosture.Closed);
}
