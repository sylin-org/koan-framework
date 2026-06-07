using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;

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
        // Phase 2 increments wire the issuer, bearer scheme, ambient Identity, IAuthorize seam,
        // and the fail-closed boot guard here. Intentionally empty in the 2b shell.
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote("SEC-0001 trust fabric: INACTIVE (Phase 2 WIP — identity core not yet wired).");
    }
}
