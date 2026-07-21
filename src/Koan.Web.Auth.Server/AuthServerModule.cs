using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Modules;
using Koan.Core.Provenance;
using Koan.Data.Core;
using Koan.Security.Trust.Issuer;
using Koan.Web.Auth.Server.Hosting;
using Koan.Web.Auth.Server.Controllers;
using Koan.Web.Auth.Server.Infrastructure;
using Koan.Web.Auth.Server.Keys;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Auth.Server.Protocol;
using Koan.Web.Extensions;

namespace Koan.Web.Auth.Server;

/// <summary>
/// SEC-0006 — the embedded OAuth 2.1 Authorization Server pillar (ARCH-0086 <see cref="KoanModule"/>). Opt-in
/// leaf (Reference = Intent): referencing this package activates the AS. It references <c>Koan.Web.Auth</c>
/// (cookie session + the chained bearer scheme) and transitively <c>Koan.Security.Trust</c> (the ES256
/// asymmetric issuer it mints with). A clean downward edge — Trust never references back up.
/// </summary>
public sealed class AuthServerModule : KoanModule
{
    /// <summary>
    /// SEC-0006 addendum (WEB-0072 P3) — the well-known public dev client the MCP Explorer's device-flow exerciser
    /// plays. Seeded Development-only (see <see cref="SeedDevExplorerClientAsync"/>); never present in production.
    /// </summary>
    public const string DevExplorerClientId = "koan-dev-explorer";

    public override void Register(IServiceCollection services)
    {
        services.AddKoanOptions<AuthServerOptions>(AuthServerOptions.SectionPath);
        // One controller owns the complete OAuth HTTP surface; protocol handlers remain transport-thin.
        services.AddKoanControllersFrom<OAuthServerController>();
        services.TryAddSingleton<Protocol.FixedWindowRateLimiter>();
        services.AddHostedService<Protocol.OAuthArtifactCleanupService>();

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

        await SeedDevExplorerClientAsync(env, options, services, ct);
    }

    /// <summary>
    /// SEC-0006 addendum (WEB-0072 P3) — seed the well-known public dev client (<see cref="DevExplorerClientId"/>)
    /// the MCP Explorer's device-flow exerciser posts against. Two-gate fail-closed, mirroring the dev-token
    /// endpoint: the <c>IHostEnvironment.IsDevelopment()</c> gate is the real safety (a guessable
    /// pre-registered client must never exist in production), with <see cref="AuthServerOptions.SeedDevClient"/> as
    /// the operator opt-out. Idempotent (seed-once). The client is <b>public</b> — its security rests on PKCE + the
    /// device-consent ceremony, not a secret — and carries no redirect URIs (the device grant never redirects).
    /// <para>
    /// <see cref="OAuthClient"/>'s static <c>Get</c>/<c>Save</c> resolve their repository through the ambient
    /// <see cref="AppHost.Current"/>. That global is bound once-per-process and is the live host in production, but
    /// across a multi-host test run it can still reference a prior, disposed host. So the seed runs inside
    /// <see cref="AppHost.PushScope"/> bound to the provider this module was <b>handed</b> for startup — the
    /// blessed flow-scoped override — instead of trusting the global. A genuine persistence failure still
    /// propagates (no swallow).
    /// </para>
    /// </summary>
    private static async Task SeedDevExplorerClientAsync(
        IHostEnvironment env, AuthServerOptions options, IServiceProvider services, CancellationToken ct)
    {
        if (!env.IsDevelopment() || !options.SeedDevClient) return;

        using var scope = AppHost.PushScope(services);

        if (await OAuthClient.Get(DevExplorerClientId, ct) is not null) return; // idempotent — already seeded

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        await new OAuthClient
        {
            Id = DevExplorerClientId,
            ClientName = "Koan Dev Explorer",
            IsDynamic = false,  // not loopback-constrained, not swept by the dynamic-client GC
            ExpiresUtc = null,  // no expiry (it is re-seeded idempotently on every dev boot)
            CreatedUtc = now,
        }.Save(ct);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddNote(env.IsDevelopment()
            ? $"Embedded OAuth 2.1 AS — ephemeral ES256 key; dev-token endpoint (GET {AuthServerRoutes.DevToken}) ENABLED."
            : "Embedded OAuth 2.1 AS — persisted + rotating ES256 key (encrypted-at-rest); dev-token endpoint is dev-only (404 here).");
    }
}
