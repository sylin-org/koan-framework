using System.Collections.Generic;

namespace Koan.Tenancy;

/// <summary>The inputs the <see cref="TenancyPreflight"/> evaluates at boot (ARCH-0099 §1). All env signals are per-host.</summary>
/// <param name="IsDevelopment">The host environment is Development — the only environment in which dev-open is legal.</param>
/// <param name="IsProduction">The host environment is Production (the deployment where a missing resolver is directly exploitable).</param>
/// <param name="PostureIsOpen">The <b>resolved</b> posture (<see cref="TenancyRuntime.Posture"/>) is Open — authoritative over how it got there (config override, programmatic override, or env).</param>
/// <param name="HasResolver">At least one <see cref="ITenantResolver"/> is registered.</param>
/// <param name="BrandedDevMarkerPresent">A dev-branded artifact (<see cref="TenancyDevBrand.Prefix"/>) was found in config.</param>
public readonly record struct TenancyPreflightInput(
    bool IsDevelopment,
    bool IsProduction,
    bool PostureIsOpen,
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
/// host. It is <b>authoritative over the resolved posture</b>, not over how the posture was requested, so it
/// catches a forced-Open whether it arrived via the config key, a programmatic <c>Configure&lt;TenancyOptions&gt;</c>,
/// or a divergent env source. The load-bearing invariant: <b>Open is legal only in Development</b> — a resolved
/// Open posture outside Development refuses the boot. Production additionally requires a real resolver. Outside
/// Production (Staging/Test) a missing resolver never blocks — the gate still fails closed, so a soft warning
/// suffices; blocking the soft cases is what drives developers to rage-disable the whole feature.
/// </summary>
public static class TenancyPreflight
{
    public static TenancyPreflightResult Evaluate(TenancyPreflightInput input)
    {
        var fails = new List<string>();
        var warns = new List<string>();

        // Invariant: a resolved Open posture is legal ONLY in Development. This is authoritative over the runtime
        // posture, so it catches every way Open can leak past dev — a config override, a programmatic override, or
        // a divergent/latched env source — in one check.
        if (input.PostureIsOpen && !input.IsDevelopment)
            fails.Add(
                "Tenancy posture resolved to Open but the host environment is not Development — dev-open is legal " +
                "only in Development. Remove the Koan:Data:Tenancy:Posture=Open override (config or code), or run " +
                "in Development. (ARCH-0099 §1)");

        // A dev-branded artifact must never appear outside Development (a leaked dev key/secret).
        if (!input.IsDevelopment && input.BrandedDevMarkerPresent)
            fails.Add(
                "A dev-branded artifact (the \"" + TenancyDevBrand.Prefix + "\" marker) is present outside " +
                "Development. Replace the dev-seeded key/secret with a real value before deploying. (ARCH-0099 §1)");

        // Production additionally requires a real resolver (without one every tenant-scoped request fails closed).
        if (input.IsProduction && !input.HasResolver)
            fails.Add(
                "Tenancy is active in Production but no tenant resolver is registered. Register an ITenantResolver " +
                "(claim / host / header) so inbound requests resolve a tenant; without one every tenant-scoped " +
                "operation fails closed. (ARCH-0099 §1)");
        else if (!input.IsDevelopment && !input.IsProduction && !input.HasResolver && !input.PostureIsOpen)
            warns.Add(
                "Tenancy is active with no ITenantResolver configured. The gate fails closed without one — register " +
                "an ITenantResolver (claim / host / header) before this reaches Production. (ARCH-0099 §1)");

        return new TenancyPreflightResult(fails, warns);
    }
}
