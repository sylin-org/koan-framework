using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Security.Trust.Bootstrap;
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
    public override string Id => "security.trust";

    public override void Register(IServiceCollection services)
    {
        // 2c — the asymmetric (ES256) dev issuer behind the IIssuer seam. Singleton: the per-process
        // keypair must be shared, or validation fails non-deterministically.
        services.AddKoanOptions<TrustIssuerOptions>(TrustIssuerOptions.SectionPath);
        services.AddSingleton<IIssuer, DevIssuer>();
        // 2e — the ambient Identity.Current reads HttpContext.User through this accessor (idempotent).
        services.AddHttpContextAccessor();
        // The inbound bearer scheme is attached by Koan.Web.Auth.AddKoanWebAuth() via AddKoanBearer (2d);
        // the resource-side IAuthorize seam lives in Koan.Web.Extensions (2f).
    }

    /// <summary>
    /// 2g — fail-closed boot guard (SEC-0001 §4.2): refuse to start in Production when the ephemeral
    /// in-process issuer would back production and no real issuer/key is configured. Throwing here fails
    /// host startup (KoanModuleHost awaits Start without catching).
    /// </summary>
    public override Task Start(IServiceProvider services, CancellationToken ct)
    {
        var env = services.GetRequiredService<IHostEnvironment>();
        var cfg = services.GetRequiredService<IConfiguration>();
        if (env.IsProduction()
            && TrustPosture.Detect(cfg) == TrustMode.DevEphemeral
            && !TrustPosture.AllowEphemeralInProduction(cfg))
        {
            throw new InvalidOperationException(
                "SEC-0001 fail-closed: running in Production with the ephemeral in-process trust issuer and no " +
                "configured issuer. The dev issuer's per-process key cannot back production (multi-instance and " +
                "restart-safe validation need a stable/shared key). Configure '" + TrustPosture.IssuerKey +
                "' (or '" + TrustPosture.SharedKeyKey + "'), or set '" + TrustPosture.AllowEphemeralInProductionKey +
                "=true' to acknowledge an ephemeral-issuer dev/staging deployment.");
        }
        return Task.CompletedTask;
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var mode = TrustPosture.Detect(cfg);
        module.AddNote($"Trust mode: {mode}");
        if (mode == TrustMode.DevEphemeral && !env.IsDevelopment())
            module.AddNote("WARNING: ephemeral in-process issuer active outside Development — configure "
                + TrustPosture.IssuerKey + " for production.");
    }
}
