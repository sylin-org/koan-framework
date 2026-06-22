using System.Collections.Generic;

namespace Koan.Tenancy;

/// <summary>The inputs the <see cref="TenancyPreflight"/> evaluates at boot (ARCH-0099 §1).</summary>
/// <param name="IsProduction">The host environment is Production (the deployment where absence is directly exploitable).</param>
/// <param name="OverrideRequestedOpen">An explicit <c>Koan:Data:Tenancy:Posture=Open</c> override is present.</param>
/// <param name="HasResolver">At least one <see cref="ITenantResolver"/> is registered.</param>
/// <param name="BrandedDevMarkerPresent">A dev-branded artifact (<see cref="TenancyDevBrand.Prefix"/>) was found in config.</param>
public readonly record struct TenancyPreflightInput(
    bool IsProduction,
    bool OverrideRequestedOpen,
    bool HasResolver,
    bool BrandedDevMarkerPresent);

/// <summary>The verdict of the boot pre-flight: hard failures refuse the boot; warnings are logged, never blocking.</summary>
public sealed record TenancyPreflightResult(IReadOnlyList<string> HardFailures, IReadOnlyList<string> Warnings)
{
    /// <summary>True when any hard failure was found — the host must refuse to boot.</summary>
    public bool ShouldRefuseBoot => HardFailures.Count > 0;
}

/// <summary>
/// The tenancy boot pre-flight (ARCH-0099 §1) — pure and deterministic so the policy is unit-testable without a
/// host. <b>Refuses to boot</b> (hard failure) only in Production, for the small set where absence is directly
/// exploitable: a forced dev-open posture, no tenant resolver, or a dev-branded artifact in production config.
/// Outside Production it never blocks — it surfaces a soft warning (the open-surfaces census), because blocking
/// the soft cases is exactly what drives developers to rage-disable the whole feature.
/// </summary>
public static class TenancyPreflight
{
    public static TenancyPreflightResult Evaluate(TenancyPreflightInput input)
    {
        var fails = new List<string>();
        var warns = new List<string>();

        if (input.IsProduction)
        {
            if (input.OverrideRequestedOpen)
                fails.Add(
                    "Tenancy posture was forced Open (Koan:Data:Tenancy:Posture=Open) while the host environment " +
                    "is Production. Dev-open in production is refused — remove the override so the posture derives " +
                    "to Closed. (ARCH-0099 §1)");

            if (!input.HasResolver)
                fails.Add(
                    "Tenancy is active in Production but no tenant resolver is registered. Register an " +
                    "ITenantResolver (claim / host / header) so inbound requests resolve a tenant; without one " +
                    "every tenant-scoped operation fails closed. (ARCH-0099 §1)");

            if (input.BrandedDevMarkerPresent)
                fails.Add(
                    "A dev-branded artifact (the \"" + TenancyDevBrand.Prefix + "\" marker) is present in a " +
                    "Production configuration. Replace the dev-seeded key/secret with a real production value " +
                    "before deploying. (ARCH-0099 §1)");
        }
        else if (!input.HasResolver && !input.OverrideRequestedOpen)
        {
            warns.Add(
                "Tenancy is active with no ITenantResolver configured. In Development the auto-seeded dev tenant " +
                "stands in, but Production will refuse to boot without one — register an ITenantResolver (claim / " +
                "host / header) before deploying. (ARCH-0099 §1)");
        }

        return new TenancyPreflightResult(fails, warns);
    }
}
