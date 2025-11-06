using Koan.Admin.Infrastructure;
using Koan.Admin.Options;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Web.Admin.Extensions;
using Koan.Web.Admin.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Koan.Web.Admin.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Admin";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanWebAdmin();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.AspNetCore.Hosting.IStartupFilter, Hosting.KoanAdminStartupFilter>());
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);

        // Read admin configuration to determine if web surfaces are enabled
        var defaults = new KoanAdminOptions();
        var enabledOption = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.Enabled}", defaults.Enabled);
        var webOption = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.EnableWeb}", defaults.EnableWeb);
        var launchKitOption = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.EnableLaunchKit}", defaults.EnableLaunchKit);
        var prefixOption = Configuration.Read(cfg, $"{ConfigurationConstants.Admin.Section}:{ConfigurationConstants.Admin.Keys.PathPrefix}", defaults.PathPrefix);

        // Report full URLs for admin endpoints if enabled
        if (enabledOption && webOption)
        {
            // Construct routes based on prefix (matching Koan.Admin's default structure)
            var prefix = (prefixOption ?? KoanAdminDefaults.Prefix).Trim('/');
            var rootPath = $"{prefix}/admin/";
            var apiPath = $"{prefix}/admin/api";
            var launchKitPath = $"{prefix}/admin/api/launchkit";

            var adminUiUrl = KoanWeb.Urls.Build(rootPath, cfg, env);
            var adminApiUrl = KoanWeb.Urls.Build(apiPath, cfg, env);

            module.AddSetting(
                WebAdminProvenanceItems.AdminUiUrl,
                ProvenancePublicationMode.Custom,
                adminUiUrl,
                sourceKey: "Resolved from ApplicationUrl and PathPrefix",
                usedDefault: false);

            module.AddSetting(
                WebAdminProvenanceItems.AdminApiUrl,
                ProvenancePublicationMode.Custom,
                adminApiUrl,
                sourceKey: "Resolved from ApplicationUrl and PathPrefix",
                usedDefault: false);

            if (launchKitOption)
            {
                var launchKitUrl = KoanWeb.Urls.Build(launchKitPath, cfg, env);
                module.AddSetting(
                    WebAdminProvenanceItems.LaunchKitUrl,
                    ProvenancePublicationMode.Custom,
                    launchKitUrl,
                    sourceKey: "Resolved from ApplicationUrl and PathPrefix",
                    usedDefault: false);
            }
        }
    }
}

