using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Web.Auth.Server.Hosting;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Hosting;

namespace Koan.Web.Auth.Server;

/// <summary>
/// SEC-0006 — the embedded OAuth 2.1 Authorization Server pillar (ARCH-0086 <see cref="KoanModule"/>). Opt-in
/// leaf (Reference = Intent): referencing this package activates the AS. It references <c>Koan.Web.Auth</c>
/// (cookie session + the chained bearer scheme) and transitively <c>Koan.Security.Trust</c> (the ES256
/// asymmetric issuer it mints with). A clean downward edge — Trust never references back up.
/// <para>
/// Phase 1 maps only the dev-token convenience endpoint; the authorize/token/device/DCR surface and the
/// consent seam land in later phases.
/// </para>
/// </summary>
public sealed class AuthServerModule : KoanModule
{
    public override string Id => "web.auth.server";

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<AuthServerOptions>(AuthServerOptions.SectionPath);
        // Map /oauth/* inside Koan's single UseEndpoints block (WEB-0069 seam) — no app ceremony.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanEndpointContributor, DevTokenEndpoint>());
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote(env.IsDevelopment()
            ? "Embedded OAuth 2.1 AS — dev-token endpoint (GET /oauth/dev-token) ENABLED in Development."
            : "Embedded OAuth 2.1 AS — dev-token endpoint is dev-only (returns 404 here).");
    }
}
