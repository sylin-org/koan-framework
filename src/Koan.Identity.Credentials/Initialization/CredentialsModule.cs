using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Registry;
using Koan.Core.Modules;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Koan.Identity.Credentials.Checkup;
using Koan.Identity.Credentials.StepUp;

namespace Koan.Identity.Credentials.Initialization;

/// <summary>
/// SEC-0007 P3-grp4 — Reference = Intent: referencing <c>Koan.Identity.Credentials</c> lights up the account-security
/// factor base — the 2-phase step-up sign-in seam (the generic <see cref="ISignInGate"/> + the discovered
/// requirement contributors) and the Security Checkup contributor read-model. The factor packages
/// (<c>Koan.Identity.Passwords</c> / <c>.Mfa</c> / <c>.Passkeys</c>) contribute over these seams. Ordered
/// <c>[After]</c> the identity module so its registrations exist first.
/// </summary>
[After(typeof(Koan.Identity.Initialization.SecIdentityModule))]
public sealed class CredentialsModule : KoanModule
{
    public override string Id => "Koan.Identity.Credentials";

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<StepUpOptions>(StepUpOptions.SectionPath);

        // 2-phase step-up: the orchestrator + the generic gate (registered as the Koan.Identity ISignInGate seam —
        // SecIdentityModule also discovers it; TryAddEnumerable dedups) + the discovered per-factor requirements.
        services.TryAddScoped<StepUpService>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISignInGate, StepUpSignInGate>());
        foreach (var type in KoanRegistry.GetDiscoveredImplementors(typeof(IStepUpRequirementContributor)))
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IStepUpRequirementContributor), type));

        // Security Checkup: the read-model resolver + the discovered per-factor signal contributors + the
        // primary-credential probes (so the MFA nudge only fires when Koan owns the primary factor — honest checkup).
        services.TryAddScoped<SecurityCheckupResolver>();
        foreach (var type in KoanRegistry.GetDiscoveredImplementors(typeof(ISecurityCheckupContributor)))
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(ISecurityCheckupContributor), type));
        foreach (var type in KoanRegistry.GetDiscoveredImplementors(typeof(IPrimaryCredentialProbe)))
            services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IPrimaryCredentialProbe), type));
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("Identity.Credentials", b => b.Value(
            "account-security factor base — 2-phase step-up sign-in seam (amr/acr) + IPasswordHasher + Security Checkup contributor read-model (SEC-0007 P3-grp4)"));
    }
}
