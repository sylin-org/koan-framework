namespace Koan.Tenancy;

/// <summary>
/// Tenancy configuration. Bound from <c>Koan:Tenancy</c>. There is no cross-environment "off"
/// switch — ARCH-0099 §1 retired <c>TenancyMode.Off</c>: referencing <c>Koan.Tenancy</c> activates tenancy
/// (Reference = Intent) and the <see cref="TenancyPosture"/>, derived from the environment, decides how strict
/// it is. The only knob here is an explicit posture <b>override</b> (a testing aid), default <c>null</c> =
/// env-derived. The tenancy kernel (P1–P3 + P7) is configured here; the multi-axis flow (jobs/messaging/cache)
/// lives above the "Magic Cliff" and is enabled by referencing those pillars.
/// </summary>
public sealed class TenancyOptions
{
    /// <summary>The standard configuration section for the Tenancy pillar.</summary>
    public const string SectionPath = "Koan:Tenancy";

    /// <summary>
    /// Explicit posture override (default <c>null</c> = derived from the current host environment).
    /// Set <see cref="TenancyPosture.Closed"/> to exercise production behavior locally. Setting
    /// <see cref="TenancyPosture.Open"/> while the environment is Production is refused at boot (ARCH-0099 §1 —
    /// a dev-open flag in prod is the exact mistake the pre-flight exists to catch).
    /// </summary>
    public TenancyPosture? Posture { get; set; }
}
