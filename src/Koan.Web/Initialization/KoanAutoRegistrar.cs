using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Web.Extensions;
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
            string.Empty);
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

