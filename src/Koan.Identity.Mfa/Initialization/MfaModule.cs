using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Ordering;
using Koan.Core.Provenance;

namespace Koan.Identity.Mfa.Initialization;

/// <summary>
/// SEC-0007 P3-grp4 — Reference = Intent: referencing <c>Koan.Identity.Mfa</c> lights up the TOTP factor + recovery
/// codes, and contributes the MFA step-up requirement (so the 2-phase sign-in now interrupts) + the MFA/recovery
/// Security Checkup nudges. Off by default (D4) — it only gates a person who has actually enrolled a confirmed factor.
/// Registers ASP.NET data protection so TOTP secrets are encrypted at rest (an app must persist its keys in
/// production). Ordered <c>[After]</c> the Credentials base so the step-up / checkup seams exist first.
/// </summary>
[After(typeof(Koan.Identity.Credentials.Initialization.CredentialsModule))]
public sealed class MfaModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<MfaOptions>(MfaOptions.SectionPath);
        services.AddDataProtection(); // encrypt the recoverable TOTP secret at rest (production must persist the keys)
        services.TryAddSingleton<IMfaSecretProtector, DataProtectionMfaSecretProtector>();
        services.TryAddScoped<TotpService>();
        services.TryAddScoped<RecoveryCodeService>();
        // The step-up requirement + the MFA/recovery checkup contributors are discovered by the Credentials base.
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("Identity.Mfa", b => b.Value(
            "TOTP second factor (secret encrypted at rest) + pre-provisioned single-use recovery codes + step-up requirement, off by default (SEC-0007 P3-grp4)"));
    }
}
