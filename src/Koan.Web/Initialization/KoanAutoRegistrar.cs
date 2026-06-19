using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Modules;
using Koan.Web.Authorization;
using Koan.Web.Extensions;
using Koan.Web.Hooks;
using Koan.Web.Infrastructure;
using Koan.Web.Pillars;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        WebPillarManifest.EnsureRegistered();
        services.AddKoanWeb();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.KoanWebStartupFilter>());

        // ARCH-0092 (§D): register the unified IAuthorize seam + the built-in entity-floor rung BY DEFAULT so
        // base CRUD (and the MCP edge) can authorize through one engine. With no provider granting/denying, the
        // ladder falls through to AuthorizeOptions.DefaultDecision (Allow) — allow-by-default is preserved.
        // Koan.Web.Extensions stacks the RBAC + named-policy rungs on top when capability authz is configured.
        services.AddKoanOptions<AuthorizeOptions>(AuthorizeOptions.SectionPath);

        // SEC-0004 origin dimension: the declared trusted-internal networks (fail-closed when unset). The request
        // builder stamps the server-trusted koan:origin claim from these + the connection's remote IP.
        services.AddKoanOptions<OriginOptions>(OriginOptions.SectionPath);

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
    }

    public void Describe(global::Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
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

        module.AddSetting(
            WebProvenanceItems.SecureHeadersEnabled,
            global::Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions.FromConfigurationValue(secure),
            secure.Value,
            sourceKey: secure.ResolvedKey,
            usedDefault: secure.UsedDefault);

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
    }
}

