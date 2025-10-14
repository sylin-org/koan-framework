using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Web.Extensions;
using Koan.Web.Pillars;

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

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var secure = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{Infrastructure.ConfigurationConstants.Web.Section}:{Infrastructure.ConfigurationConstants.Web.Keys.EnableSecureHeaders}",
            true);
        var proxied = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{Infrastructure.ConfigurationConstants.Web.Section}:{Infrastructure.ConfigurationConstants.Web.Keys.IsProxiedApi}",
            false);
        var csp = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{Infrastructure.ConfigurationConstants.Web.Section}:{Infrastructure.ConfigurationConstants.Web.Keys.ContentSecurityPolicy}",
            default(string?));
        var autoMap = Koan.Core.Configuration.ReadWithSource(
            cfg,
            $"{Infrastructure.ConfigurationConstants.Web.Section}:{Infrastructure.ConfigurationConstants.Web.Keys.AutoMapControllers}",
            true);

        report.AddSetting(
            "EnableSecureHeaders",
            secure.Value.ToString(),
            source: secure.Source,
            consumers: new[] { "Koan.Web.SecurityHeaders" },
            sourceKey: secure.ResolvedKey);

        report.AddSetting(
            "IsProxiedApi",
            proxied.Value.ToString(),
            source: proxied.Source,
            consumers: new[] { "Koan.Web.HttpPipeline" },
            sourceKey: proxied.ResolvedKey);

        report.AddSetting(
            "AutoMapControllers",
            autoMap.Value.ToString(),
            source: autoMap.Source,
            consumers: new[] { "Koan.Web.ControllerDiscovery" },
            sourceKey: autoMap.ResolvedKey);

        if (!string.IsNullOrWhiteSpace(csp.Value))
        {
            report.AddSetting(
                "ContentSecurityPolicy",
                $"len={csp.Value!.Length}",
                source: csp.Source,
                consumers: new[] { "Koan.Web.SecurityHeaders" },
                sourceKey: csp.ResolvedKey);
        }

        report.AddTool(
            "Health Probes",
            $"/{Infrastructure.KoanWebConstants.Routes.HealthBase}",
            "Readiness and liveness endpoints exposed by Koan.Web",
            capability: "observability.health");
    }
}
