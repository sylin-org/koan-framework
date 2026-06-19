using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
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
        // SEC-0004: the per-entity gate cache (lazily compiles [Access] + lowered legacy floor sugar) feeds the
        // floor rung; a singleton so the parse/lowering happens once per entity type.
        services.AddKoanOptions<AuthorizeOptions>(AuthorizeOptions.SectionPath);
        services.TryAddSingleton<IAccessGateCache, AccessGateCache>();
        services.TryAddScoped<IAuthorize, Authorizer>();
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

