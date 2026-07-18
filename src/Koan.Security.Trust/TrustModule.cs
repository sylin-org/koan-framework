using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Security.Trust.Bootstrap;
using Koan.Security.Trust.Dev;
using Koan.Security.Trust.Issuer;

namespace Koan.Security.Trust;

/// <summary>
/// SEC-0001 — the fleet identity and trust fabric pillar (ARCH-0086 <see cref="KoanModule"/>).
/// <para>
/// This is the lower-level pillar that owns the asymmetric issuer, the inbound token verifier
/// (<c>"Koan.bearer"</c> scheme), the ambient <c>Identity</c>, the <c>IAuthorize</c> seam, and the
/// fail-closed trust-mode boot guard. <c>Koan.Web.Auth</c> references this pillar one-way and calls into
/// it to attach the bearer scheme alongside its cookie scheme — Trust never references back up.
/// </para>
/// <para>
/// Increment 2b (this commit) is the additive shell: it registers nothing yet and reports an inactive
/// posture. Subsequent Phase-2 increments (2c issuer, 2d bearer scheme, 2e ambient, 2f IAuthorize,
/// 2g boot guard) fill in the core without reshaping this module's identity or ordering.
/// </para>
/// </summary>
public sealed class TrustModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // SEC-0003 — the shared-secret (HS256) issuer behind the IIssuer seam. Singleton. Signs with
        // SHA-256(Koan:Security:Trust:Key); the key defaults to the well-known insecure value so every service
        // self-mints zero-config (and they trust each other). Fail-closed outside Development (see Start).
        services.AddKoanOptions<TrustIssuerOptions>(TrustIssuerOptions.SectionPath);
        services.AddSingleton<IIssuer, SharedKeyIssuer>();
        // SEC-0006 D1 — the asymmetric (ES256) issuer tier, exposed through its own seam so it never hijacks
        // the default IIssuer (HS256 service-mesh) resolution. The embedded Authorization Server, the JWKS
        // endpoint, and the inbound bearer scheme resolve IAsymmetricIssuer; the default key store is an
        // ephemeral per-process keypair (a persisted, encrypted-at-rest store overrides it via TryAdd —
        // Reference = Intent). Zero key config: the asymmetric tier "just works" with an unforgeable key.
        services.TryAddSingleton<IIssuerKeyStore, EphemeralIssuerKeyStore>();
        services.AddSingleton<IAsymmetricIssuer, EcdsaIssuer>();
        // 2e — the ambient Identity.Current reads HttpContext.User through this accessor (idempotent).
        services.AddHttpContextAccessor();
        // Rung 0 — zero-config dev identity options. The middleware is inserted by the auth pipeline in
        // Development only (the Web Auth pipeline contributor), so it is structurally absent in production.
        services.AddKoanOptions<DevIdentityOptions>(DevIdentityOptions.SectionPath);
        // The inbound bearer scheme is attached by Koan.Web.Auth.AddKoanWebAuth() via AddKoanBearer (2d);
        // the resource-side IAuthorize seam lives in Koan.Web.Extensions (2f).
    }

    /// <summary>
    /// SEC-0003 §2.5 — fail-closed boot guard + the very loud dev warning. Refuse to start when the
    /// environment is NOT Development and the default insecure shared secret is still in use (unless
    /// explicitly acknowledged). Throwing here fails host startup (KoanModuleHost awaits Start without
    /// catching). While the default key IS active (allowed only in dev), emit a loud, framed warning banner.
    /// </summary>
    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var env = services.GetRequiredService<IHostEnvironment>();
        var cfg = services.GetRequiredService<IConfiguration>();
        var mode = TrustPosture.Detect(cfg);

        // Real-deployment environments (Production / Staging) must not run on the public default key. Development
        // and test environments (e.g. "Test", "Testing") boot with the loud warning below.
        if (mode == TrustMode.DefaultInsecure && (env.IsProduction() || env.IsStaging()) && !TrustPosture.AllowInsecureKeyInProduction(cfg))
        {
            throw new InvalidOperationException(
                "SEC-0003 fail-closed: environment '" + env.EnvironmentName + "' is using the DEFAULT INSECURE shared " +
                "secret ('" + TrustIssuerOptions.DefaultInsecureKey + "'), which is public and forgeable. Set '" +
                TrustPosture.SharedKeyKey + "' to a real secret (or '" + TrustPosture.IssuerKey + "' for a fleet " +
                "issuer), or set '" + TrustPosture.AllowInsecureKeyInProductionKey + "=true' to acknowledge a " +
                "throwaway deployment.");
        }

        if (mode == TrustMode.DefaultInsecure)
            services.GetService<ILoggerFactory>()?.CreateLogger("Koan.Security.Trust").LogWarning("{Banner}", DefaultKeyWarningBanner);

        return Task.CompletedTask;
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var mode = TrustPosture.Detect(cfg);
        module.AddNote($"Trust mode: {mode}");
        if (mode == TrustMode.DefaultInsecure)
            module.AddNote("WARNING: default INSECURE shared secret in use — set " + TrustPosture.SharedKeyKey
                + " to a real secret (required outside Development).");
    }

    // SEC-0003 §2.5 — impossible to miss on every dev start while the public default key is active.
    private const string DefaultKeyWarningBanner =
        "\n" +
        "================================================================================\n" +
        "  ⚠  KOAN TRUST — DEFAULT INSECURE SHARED SECRET IN USE  ⚠\n" +
        "  Every service self-mints tokens with a PUBLIC, well-known key:\n" +
        "      '" + TrustIssuerOptions.DefaultInsecureKey + "'\n" +
        "  Fine for local development — NEVER for shared, staging, or production use.\n" +
        "  ->  Set 'Koan:Security:Trust:Key' to a real secret before deploying.\n" +
        "================================================================================";
}
