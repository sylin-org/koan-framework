using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Composition;
using Koan.Core.Extensions;
using Koan.Core.Modules;
using Koan.Web.Authorization;
using Koan.Web.Extensions;
using Koan.Web.Hooks;
using Koan.Web.Infrastructure;
using Koan.Web.Pillars;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Ordering;
using Koan.Data.Core.Pipeline;

namespace Koan.Web.Initialization;

// WEB-0081: Start reads the application's IApplicationUrlStore, which commonly reads an Entity<T> row, so it runs after
// the data pillar has started (default-route hydration and the axis pre-flight) whatever the module ids sort to.
[After(typeof(Koan.Data.Core.Initialization.KoanDataCoreModule))]
public sealed class WebModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        WebPillarManifest.EnsureRegistered();
        services.AddKoanWeb();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.KoanWebStartupFilter>());

        // WEB-0078: the one canonical base URL. Parsed once from Koan:Web:ApplicationUrl; the startup filter serves
        // under its path and every pillar that writes an absolute URL reads it from here. An unparseable value is a
        // corrective boot failure, not a request-time surprise. WEB-0081: the application's own IApplicationUrlStore,
        // when it registers one, holds the URL it accepted at runtime; Start loads it before the host serves.
        services.TryAddSingleton(sp => new Hosting.KoanWebBaseUrl(
            sp.GetRequiredService<IOptions<Options.KoanWebOptions>>().Value.ApplicationUrl,
            sp.GetService<IHostEnvironment>(),
            sp.GetService<ILogger<Hosting.KoanWebBaseUrl>>(),
            sp.GetService<Hosting.IApplicationUrlStore>(),
            sp.GetService<Koan.Core.Diagnostics.IKoanRuntimeFactRecorder>(),
            () => sp.GetServices<Hosting.IApplicationUrlRule>()));
        // WEB-0081: the origins requests arrive on, noted at the head of the pipeline and held in memory, bounded.
        services.TryAddSingleton(sp => new Hosting.KoanWebOrigins(
            sp.GetRequiredService<Hosting.KoanWebBaseUrl>(),
            sp.GetRequiredService<IOptions<Options.KoanWebOptions>>().Value.ObservedOriginLimit,
            sp.GetService<TimeProvider>(),
            sp.GetService<ILogger<Hosting.KoanWebOrigins>>()));
        // ARCH-0144: the host's whole pipeline, held by decorating ASP.NET's own builder factory so it sits outside every middleware and
        // startup filter, answers a request made in process; a global limiter counts the request in hand once.
        services.AddHttpContextAccessor();
        services.TryAddSingleton<Hosting.KoanWebPipeline>();
        services.Replace(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.Builder.IApplicationBuilderFactory>(
            sp => sp.GetRequiredService<Hosting.KoanWebPipeline>()));
        services.PostConfigure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(limiter =>
        {
            if (limiter.GlobalLimiter is { } global) limiter.GlobalLimiter = new Hosting.OneCountLimiter(global);
        });
        // WEB-0079: where the Data Protection key ring lives. One owner reads Koan:Web:KeyRingDirectory; when it is set
        // the ring persists there under the application name the folder remembers, and Start proves the folder usable
        // before anything is protected. The owner supplies defaults the application's own configuration overrides: the
        // key repository when none is configured, and the application discriminator ASP.NET falls back to, which
        // delegates to the host's own when the key is unset. Unset, nothing changes.
        services.AddDataProtection();
        var hostDiscriminator = services.LastOrDefault(static descriptor =>
            descriptor.ServiceType == typeof(Microsoft.AspNetCore.DataProtection.Infrastructure.IApplicationDiscriminator) && !descriptor.IsKeyedService);
        services.TryAddSingleton(sp => new Hosting.KoanWebKeyRing(
            sp,
            sp.GetRequiredService<IOptions<Options.KoanWebOptions>>(),
            sp.GetService(typeof(Koan.Core.Hosting.App.ApplicationIdentitySnapshot)) is Koan.Core.Hosting.App.ApplicationIdentitySnapshot identity
                ? identity.Code
                : Koan.Core.Hosting.App.ApplicationIdentitySnapshot.Empty.Code,
            sp.GetService<IHostEnvironment>(),
            sp.GetService<ILoggerFactory>(),
            hostDiscriminator is null ? null : (Microsoft.AspNetCore.DataProtection.Infrastructure.IApplicationDiscriminator)Instantiate(sp, hostDiscriminator)));
        services.AddSingleton<IConfigureOptions<Microsoft.AspNetCore.DataProtection.KeyManagement.KeyManagementOptions>>(
            sp => sp.GetRequiredService<Hosting.KoanWebKeyRing>());
        services.AddSingleton<Microsoft.AspNetCore.DataProtection.Infrastructure.IApplicationDiscriminator>(
            sp => sp.GetRequiredService<Hosting.KoanWebKeyRing>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Hosting.IKoanWebPipelineContributor, Context.WebContextPipelineContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IReadFilterContributor, Context.WebContextReadFilterContributor>());

        // ARCH-0092 (§D): register the unified IAuthorize seam + the built-in entity-floor rung BY DEFAULT so
        // base CRUD (and the MCP edge) can authorize through one engine. With no provider granting/denying, the
        // ladder falls through to AuthorizeOptions.DefaultDecision (Allow) — allow-by-default is preserved.
        // Koan.Web.Extensions stacks the RBAC + named-policy rungs on top when capability authz is configured.
        services.AddKoanOptions<AuthorizeOptions>(AuthorizeOptions.SectionPath);

        // SEC-0004 origin dimension: the declared trusted-internal networks (fail-closed when unset). The request
        // builder stamps the server-trusted koan:origin claim from these + the connection's remote IP.
        services.AddKoanOptions<OriginOptions>(OriginOptions.SectionPath);

        // Pagination safety bounds are a ceiling over every EntityController, so they bind with the
        // reference rather than waiting on an application call, and normalize after binding.
        services.AddKoanOptions<PaginationSafetyBounds>(PaginationSafetyBounds.SectionPath);
        services.PostConfigure<PaginationSafetyBounds>(bounds => bounds.Normalize());

        // Cache-control headers steer server-side caching, so the posture is a gate decision rather than a
        // pipeline call the application has to place correctly. See ARCH-0133.
        services.AddKoanOptions<KoanCacheControlOptions>(KoanCacheControlOptions.SectionPath);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Hosting.IKoanWebPipelineContributor, Middleware.CacheControlPipelineContributor>());

        // SEC-0004 Slice B: discover EntityAccess<T> realizations once (the same discovery authority every Koan
        // contract uses). The gate cache reads each realization's principal-FREE gate; the endpoint resolves the
        // SCOPED realization for Constrain; and an open-generic read hook rides the WEB-0068 predicate rail. A type
        // with no realization is a no-op in every consumer (the backward-compat contract).
        var accessRegistry = Authorization.EntityAccessRegistry.FromDiscovery();
        services.TryAddSingleton(accessRegistry);
        services.TryAddSingleton<IAccessGateCache>(sp =>
            new AccessGateCache(sp.GetService<ILogger<AccessGateCache>>(), accessRegistry.For));
        foreach (var (entity, realization) in accessRegistry.All())
        {
            services.TryAddScoped(typeof(Authorization.EntityAccess<>).MakeGenericType(entity), realization);
        }
        services.TryAddEnumerable(ServiceDescriptor.Scoped(
            typeof(Koan.Web.Hooks.IRequestOptionsHook<>), typeof(Authorization.EntityAccessConstrainHook<>)));

        services.TryAddScoped<IAuthorize, Authorizer>();
        // SEC-0005: server-side AgentGrants enrich the gate decision on the token-denied path. Scoped = memoized per
        // request (fresh each request → Remove()/expiry revoke on the next call). No grants declared = inert.
        services.TryAddScoped<IAgentGrantStore, AgentGrantStore>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationProvider, EntityFloorAuthorizationProvider>());

        // SEC-0004: fail fast at boot — compile every [Access] declaration now and aggregate all malformed ones
        // into one exception, so a typo can never reach production as a silently-open or silently-denied gate.
        Authorization.AccessGateRegistrar.Validate();

        // Ensure MVC discovers controllers from this assembly
        services.AddKoanControllersFrom<Controllers.HealthController>();
        Transformers.Initialization.WebTransformersBootstrap.Register(services);
    }

    // WEB-0079: the key ring's folder is created, proved writable and loaded before the host serves, so a folder this
    // process cannot use is a corrective start failure rather than a failed first sign-in. WEB-0081: the accepted base
    // URL is read from the application's store before the host serves, inside the host's ambient scope so a store over
    // an Entity reads this host's data; a stored value this host cannot use is set aside with one warning, never a
    // refused start, so a place moved between environments starts as if nothing were accepted.
    public override async Task Start(IServiceProvider services, CancellationToken ct)
    {
        services.GetRequiredService<Hosting.KoanWebKeyRing>().Prepare();
        using var scope = Koan.Core.Hosting.App.AppHost.PushScope(services);
        await services.GetRequiredService<Hosting.KoanWebBaseUrl>().Load(ct).ConfigureAwait(false);
    }

    private static object Instantiate(IServiceProvider services, ServiceDescriptor descriptor)
        => descriptor.ImplementationInstance
           ?? descriptor.ImplementationFactory?.Invoke(services)
           ?? ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!);

    // SEC-0020: state the posture an undeclared entity or verb resolves to, so Allow (the default) is read rather
    // than inferred. Narrowing is Koan:Web:Authorization:DefaultDecision=Forbid, [Access] or an EntityAccess<T>.
    public override void ReportComposition(KoanCompositionBuilder composition, IServiceProvider services)
    {
        var decision = services.GetService<IOptions<AuthorizeOptions>>()?.Value.DefaultDecision ?? AuthorizeDefault.Allow;
        composition.AddObservation(
            Infrastructure.KoanWebConstants.Authorization.DefaultDecisionFactCode,
            "web:authorization",
            "An entity or verb that declares no [Access] gate, EntityAccess<T> or authorization provider resolves to " +
            $"{decision} (Koan:Web:Authorization:DefaultDecision).",
            Infrastructure.KoanWebConstants.Authorization.DefaultDecisionFactReason,
            typeof(WebModule).FullName ?? nameof(WebModule));

        // WEB-0078: state the base URL the host serves under, so an operator reads it instead of inferring it. WEB-0081:
        // the base URL states it again whenever the application's accepted URL is loaded or changes.
        var (summary, reason) = services.GetRequiredService<Hosting.KoanWebBaseUrl>().Statement();
        composition.AddObservation(
            Infrastructure.KoanWebConstants.BaseUrl.FactCode,
            Infrastructure.KoanWebConstants.BaseUrl.FactSubject,
            summary,
            reason,
            typeof(WebModule).FullName ?? nameof(WebModule));

        // WEB-0079: state where the key ring lives, as in effect, so an operator reads it instead of discovering it at a
        // rebuild, including when the application's own Data Protection configuration replaced Koan's folder or name.
        var keyRing = services.GetRequiredService<Hosting.KoanWebKeyRing>();
        composition.AddObservation(
            Infrastructure.KoanWebConstants.KeyRing.FactCode,
            Infrastructure.KoanWebConstants.KeyRing.FactSubject,
            keyRing.Statement,
            keyRing.FactReason,
            typeof(WebModule).FullName ?? nameof(WebModule));
    }

    public override void Report(global::Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        var secure = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.EnableSecureHeaders}",
            true);
        var proxied = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.IsProxiedApi}",
            false);
        var csp = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.ContentSecurityPolicy}",
            "");
        var autoMap = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.AutoMapControllers}",
            true);
        var applicationUrl = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.ApplicationUrl}",
            "");
        var keyRingDirectory = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.KeyRingDirectory}",
            "");
        var keyRingApplicationName = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.KeyRingApplicationName}",
            "");
        var trustedProxies = cfg.GetSection($"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.TrustedProxies}")
            .GetChildren().Select(child => child.Value).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

        module.AddSetting(
            WebProvenanceItems.SecureHeadersEnabled,
            global::Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions.FromConfigurationValue(secure),
            secure.Value,
            sourceKey: secure.ResolvedKey,
            usedDefault: secure.UsedDefault);

        var crossSite = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.RefuseCrossSiteWrites}",
            true);
        module.AddSetting(
            WebProvenanceItems.RefuseCrossSiteWrites,
            global::Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions.FromConfigurationValue(crossSite),
            crossSite.Value,
            sourceKey: crossSite.ResolvedKey,
            usedDefault: crossSite.UsedDefault);

        module.AddSetting(
            WebProvenanceItems.ProxiedApi,
            global::Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions.FromConfigurationValue(proxied),
            proxied.Value,
            sourceKey: proxied.ResolvedKey,
            usedDefault: proxied.UsedDefault);

        module.AddSetting(
            WebProvenanceItems.AutoMapControllers,
            global::Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions.FromConfigurationValue(autoMap),
            autoMap.Value,
            sourceKey: autoMap.ResolvedKey,
            usedDefault: autoMap.UsedDefault);

        if (!string.IsNullOrWhiteSpace(applicationUrl.Value))
        {
            module.AddSetting(
                WebProvenanceItems.ApplicationUrl,
                global::Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions.FromConfigurationValue(applicationUrl),
                Hosting.KoanWebBaseUrl.TryNormalize(applicationUrl.Value, out var normalizedUrl) ? normalizedUrl : applicationUrl.Value,
                sourceKey: applicationUrl.ResolvedKey,
                usedDefault: applicationUrl.UsedDefault);
        }

        if (!string.IsNullOrWhiteSpace(keyRingDirectory.Value))
        {
            module.AddSetting(
                WebProvenanceItems.KeyRingDirectory,
                global::Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions.FromConfigurationValue(keyRingDirectory),
                keyRingDirectory.Value,
                sourceKey: keyRingDirectory.ResolvedKey,
                usedDefault: keyRingDirectory.UsedDefault);
        }

        if (!string.IsNullOrWhiteSpace(keyRingApplicationName.Value))
        {
            module.AddSetting(
                WebProvenanceItems.KeyRingApplicationName,
                global::Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions.FromConfigurationValue(keyRingApplicationName),
                keyRingApplicationName.Value,
                sourceKey: keyRingApplicationName.ResolvedKey,
                usedDefault: keyRingApplicationName.UsedDefault);
        }

        if (trustedProxies.Length > 0)
        {
            module.AddSetting(
                WebProvenanceItems.TrustedProxies,
                global::Koan.Core.Hosting.Bootstrap.ProvenancePublicationMode.Settings,
                string.Join(", ", trustedProxies),
                sourceKey: $"{ConfigurationConstants.Web.Section}:{ConfigurationConstants.Web.Keys.TrustedProxies}",
                usedDefault: false);
        }

        if (!string.IsNullOrWhiteSpace(csp.Value))
        {
            module.AddSetting(
                WebProvenanceItems.ContentSecurityPolicy,
                global::Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions.FromConfigurationValue(csp),
                csp.Value,
                sourceKey: csp.ResolvedKey,
                usedDefault: csp.UsedDefault);
        }

        module.AddTool(
            "Health Probes",
            $"/{Infrastructure.KoanWebConstants.Routes.HealthBase}",
            "Readiness and liveness endpoints exposed by Koan.Web",
            capability: "observability.health");
        Transformers.Initialization.WebTransformersBootstrap.Report(module, cfg, env);
    }
}

