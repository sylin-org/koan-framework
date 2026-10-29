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
using Koan.Web.Hosting;
using Koan.Web.Infrastructure;

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
        // SEC-0023 — the issuer the metadata states is the one Trust stamps on every token and Koan.bearer accepts.
        services.TryAddSingleton<AuthServerIssuer>();
        services.TryAddSingleton<IIssuerIdentifier>(sp => sp.GetRequiredService<AuthServerIssuer>());
        services.AddHostedService<Protocol.OAuthArtifactCleanupService>();

        // SEC-0006 D1 / SEC-0022 — the key lifecycle. The persisted, encrypted-at-rest, rotating ES256 store is the
        // active tier outside Development (so tokens survive restart and the JWKS is stable), and in Development too
        // when the application names a persistent key ring folder (WEB-0079); otherwise Development keeps the
        // zero-config ephemeral key (Trust's default). AddDataProtection guarantees the at-rest protector exists.
        services.AddDataProtection();
        services.AddSingleton<PersistedIssuerKeyStore>();
        services.Replace(ServiceDescriptor.Singleton<IIssuerKeyStore>(sp =>
            PersistsSigningKeys(sp.GetRequiredService<IHostEnvironment>(), sp.GetService<KoanWebKeyRing>()?.IsConfigured == true)
                ? sp.GetRequiredService<PersistedIssuerKeyStore>()
                : new EphemeralIssuerKeyStore()));
        services.AddHostedService<IssuerKeyRotationService>();
    }

    /// <summary>
    /// SEC-0022 — whether the signing keys persist: always outside Development, and in Development when the
    /// application names a persistent key ring folder (<c>Koan:Web:KeyRingDirectory</c>, WEB-0079), so a local install's
    /// tokens outlive a restart exactly where its cookies do. Without a folder, Development keeps an ephemeral key.
    /// </summary>
    internal static bool PersistsSigningKeys(IHostEnvironment env, bool keyRingNamed)
        => keyRingNamed || !KoanEnv.Gate.DevelopmentOnly(env);

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
    /// endpoint: the <see cref="KoanEnv.Gate.DevelopmentOnly"/> gate is the real safety (a guessable
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
        if (!KoanEnv.Gate.DevelopmentOnly(env) || !options.SeedDevClient) return;

        using var scope = AppHost.PushScope(services);

        var existing = await OAuthClient.Get(DevExplorerClientId, ct);
        if (existing is not null)
        {
            // Idempotent — already seeded. A row seeded before clients named their grants is given the device grant.
            if (existing.AllowsGrant(OAuthGrantTypes.DeviceCode)) return;
            existing.GrantTypes = [.. existing.GrantTypes, OAuthGrantTypes.DeviceCode];
            await existing.Save(ct);
            return;
        }

        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        await new OAuthClient
        {
            Id = DevExplorerClientId,
            ClientName = "Koan Dev Explorer",
            GrantTypes = [OAuthGrantTypes.DeviceCode], // the exerciser plays the device grant, and only that
            IsDynamic = false,  // not loopback-constrained, not swept by the dynamic-client GC
            ExpiresUtc = null,  // no expiry (it is re-seeded idempotently on every dev boot)
            CreatedUtc = now,
        }.Save(ct);
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var keyRingNamed = !string.IsNullOrWhiteSpace(cfg[$"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.KeyRingDirectory}"]);
        var keys = PersistsSigningKeys(env, keyRingNamed)
            ? "persisted + rotating ES256 key (encrypted-at-rest)"
            : "ephemeral ES256 key (name Koan:Web:KeyRingDirectory to keep it across restarts)";
        module.AddNote(KoanEnv.Gate.DevelopmentOnly(env)
            ? $"Embedded OAuth 2.1 AS — {keys}; dev-token endpoint (GET {AuthServerRoutes.DevToken}) ENABLED."
            : $"Embedded OAuth 2.1 AS — {keys}; dev-token endpoint is dev-only (404 here).");
    }
}
