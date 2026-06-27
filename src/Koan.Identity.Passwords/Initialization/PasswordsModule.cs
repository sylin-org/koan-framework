using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Ordering;
using Koan.Core.Provenance;
using Koan.Identity.Credentials;

namespace Koan.Identity.Passwords.Initialization;

/// <summary>SEC-0007 P3-grp4 — bcrypt password-hashing options (bound from <c>Koan:Identity:Passwords</c>).</summary>
public sealed class PasswordHashingOptions
{
    public const string SectionPath = "Koan:Identity:Passwords";

    /// <summary>The bcrypt cost (log2 rounds). Default 12 — a sane 2025 floor; raising it triggers upgrade-on-verify rehash.</summary>
    public int WorkFactor { get; set; } = 12;
}

/// <summary>
/// SEC-0007 P3-grp4 — Reference = Intent: referencing <c>Koan.Identity.Passwords</c> registers the BCrypt
/// <see cref="IPasswordHasher"/> (the default behind the seam) + the password credential service. Off by default
/// (D4) — it only does anything once an app sets a password on a person. The Checkup contributor is discovered by the
/// Credentials base. Ordered <c>[After]</c> the Credentials base so the seam exists first.
/// </summary>
[After(typeof(Koan.Identity.Credentials.Initialization.CredentialsModule))]
public sealed class PasswordsModule : KoanModule
{
    public override string Id => "Koan.Identity.Passwords";

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<PasswordHashingOptions>(PasswordHashingOptions.SectionPath);
        services.TryAddSingleton<IPasswordHasher>(sp =>
            new BcryptPasswordHasher(sp.GetService<IOptions<PasswordHashingOptions>>()?.Value.WorkFactor ?? 12));
        services.TryAddScoped<PasswordCredentialService>();
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.SetSetting("Identity.Passwords", b => b.Value(
            "local password factor — portable BCrypt hash + upgrade-on-verify rehash, off by default (SEC-0007 P3-grp4)"));
    }
}
