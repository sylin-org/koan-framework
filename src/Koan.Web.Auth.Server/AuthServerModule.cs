using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Server.Hosting;
using Koan.Web.Auth.Server.Keys;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Hosting;

namespace Koan.Web.Auth.Server;

/// <summary>
/// SEC-0006 — the embedded OAuth 2.1 Authorization Server pillar (ARCH-0086 <see cref="KoanModule"/>). Opt-in
/// leaf (Reference = Intent): referencing this package activates the AS. It references <c>Koan.Web.Auth</c>
/// (cookie session + the chained bearer scheme) and transitively <c>Koan.Security.Trust</c> (the ES256
/// asymmetric issuer it mints with). A clean downward edge — Trust never references back up.
/// <para>
/// Phase 1 maps the dev-token convenience endpoint and installs the production key lifecycle (persisted,
/// encrypted-at-rest, auto-rotating ES256 keys outside Development; ephemeral in Development). The
/// authorize/token/device/DCR surface and the consent seam land in later phases.
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanEndpointContributor, Protocol.OAuthProtocolEndpoints>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanEndpointContributor, Protocol.WellKnownEndpoints>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKoanEndpointContributor, Protocol.DcrEndpoint>());
        services.TryAddSingleton<Protocol.FixedWindowRateLimiter>();

        // SEC-0006 D1 — the key lifecycle. The persisted, encrypted-at-rest, rotating ES256 store is the active
        // tier OUTSIDE Development (so tokens survive restart and the JWKS is stable); Development keeps the
        // zero-config ephemeral key (Trust's default). Replace the Trust default with an environment-aware
        // factory; AddDataProtection guarantees the at-rest protector exists.
        services.AddDataProtection();
        services.AddSingleton<PersistedIssuerKeyStore>();
        services.Replace(ServiceDescriptor.Singleton<IIssuerKeyStore>(sp =>
            sp.GetRequiredService<IHostEnvironment>().IsDevelopment()
                ? new EphemeralIssuerKeyStore()
                : sp.GetRequiredService<PersistedIssuerKeyStore>()));
        services.AddHostedService<IssuerKeyRotationService>();
    }

    /// <summary>
    /// SEC-0006 D1 fail-closed boot guard + the persisted-key initialization. Outside Development the AS must
    /// run on a persisted key — refuse to start on the ephemeral key (a restart would invalidate every issued
    /// token; the JWKS would be unstable), unless explicitly acknowledged. Then load/generate the active
    /// persisted key (failing closed if the data layer is unavailable).
    /// </summary>
    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        var env = services.GetRequiredService<IHostEnvironment>();
        var options = services.GetRequiredService<IOptions<AuthServerOptions>>().Value;
        var store = services.GetRequiredService<IIssuerKeyStore>();

        IssuerKeyGuard.EnsurePersistedOutsideDevelopment(
            isEphemeral: store is EphemeralIssuerKeyStore,
            env,
            acknowledged: options.AllowEphemeralKeyOutsideDevelopment);

        if (store is PersistedIssuerKeyStore persisted)
            await persisted.InitializeAsync(ct);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote(env.IsDevelopment()
            ? "Embedded OAuth 2.1 AS — ephemeral ES256 key; dev-token endpoint (GET /oauth/dev-token) ENABLED."
            : "Embedded OAuth 2.1 AS — persisted + rotating ES256 key (encrypted-at-rest); dev-token endpoint is dev-only (404 here).");
    }
}
